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
    /// Screen lighting: a glowing frame around the desktop that reacts to whatever the PC is playing.
    /// Six looks — Ambient (a bright head sweeping round a rounded frame), Static, Dynamic,
    /// Aurora (curtain bars), Rays (streaks shooting inwards) and Tunnel (neon rings).
    /// The window is transparent, never takes focus, lets every click through, and only covers the
    /// usable desktop so the taskbar is never touched.
    /// </summary>
    public sealed class GlowWindow : Window
    {
        private sealed class Blob
        {
            public Ellipse Shape = null!;
            public RadialGradientBrush Brush = null!;
            public double U, Wobble, Size;
        }

        private sealed class Bar
        {
            public Rectangle Shape = null!, Halo = null!;
            public LinearGradientBrush Brush = null!, HaloBrush = null!;
            public int Edge;              // 0 top, 1 right, 2 bottom, 3 left
            public double Pos;            // 0..1 along that edge
            public double Seed, Len, Phase;
        }

        private sealed class Ring
        {
            public Rectangle[] Shapes = null!;        // 3 strokes stacked = neon tube bloom
            public SolidColorBrush[] Brushes = null!;
        }

        private const int BlobCount = 56;
        private const int BarCount = 72;
        private const int RingCount = 6;

        private readonly Canvas _canvas = new();
        private readonly List<Blob> _blobs = new();
        private readonly List<Bar> _bars = new();
        private readonly List<Ring> _rings = new();
        private AudioAnalyzer? _audio;

        private List<Color> _palette = new() { Color.FromRgb(255, 42, 85), Color.FromRgb(53, 255, 138), Color.FromRgb(45, 123, 255) };
        private string _style = "ambient";
        private double _hueStep;
        private bool _albumMode;
        private Color _album = Color.FromRgb(255, 170, 60);
        private double _react = 0.7, _speed = 0.35, _reach = 0.45, _bright = 0.8, _grad = 0.85;

        private double _w, _h;
        private double _flow, _hue, _bass, _kick, _ringPhase;
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

            // --- soft blobs: the glowing band that hugs the rounded frame (ambient / static / dynamic)
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
                _blobs.Add(new Blob { Shape = el, Brush = brush, U = i / (double)BlobCount, Wobble = i * 1.7, Size = 0.85 + ((i * 53) % 10) / 30.0 });
            }

            // --- bars: aurora curtain and rays, standing on each edge and reaching inwards.
            // Each bar is a soft wide halo with a brighter core on top, so nothing looks like a hard block.
            for (int i = 0; i < BarCount; i++)
            {
                LinearGradientBrush MakeBrush() => new()
                {
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0.35),
                        new GradientStop(Colors.Transparent, 0.75), new GradientStop(Colors.Transparent, 1),
                    },
                };
                var haloBrush = MakeBrush();
                var halo = new Rectangle { Fill = haloBrush, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                var brush = MakeBrush();
                var rc = new Rectangle { Fill = brush, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _canvas.Children.Add(halo);
                _canvas.Children.Add(rc);
                _bars.Add(new Bar { Shape = rc, Halo = halo, Brush = brush, HaloBrush = haloBrush, Seed = (i * 97 % 101) / 101.0, Phase = (i * 61 % 97) / 97.0 });
            }

            // --- rings: neon tubes receding inwards (tunnel)
            for (int i = 0; i < RingCount; i++)
            {
                var shapes = new Rectangle[3];
                var brushes = new SolidColorBrush[3];
                for (int k = 0; k < 3; k++)
                {
                    brushes[k] = new SolidColorBrush(Colors.Transparent);
                    shapes[k] = new Rectangle { Stroke = brushes[k], Fill = null, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                    _canvas.Children.Add(shapes[k]);
                }
                _rings.Add(new Ring { Shapes = shapes, Brushes = brushes });
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
            _style = IsStyle(s.Style) ? s.Style : "ambient";
            _albumMode = string.Equals(s.Mode, "album", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsStyle(string? s) =>
            s == "ambient" || s == "static" || s == "dynamic" || s == "aurora" || s == "rays" || s == "tunnel";

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

        private static Color Hot(Color c, double t) =>      // push a colour towards white for the hot core of a sweep
            t <= 0 ? c : Lerp(c, Colors.White, Math.Min(1, t));

        private Color PaletteAt(double p)
        {
            if (_albumMode)
            {
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

        private double CornerRadius => Math.Min(0.13 * Math.Min(_w, _h), Math.Min(_w, _h) / 2);

        /// <summary>Point and inward normal at fraction u around a rounded-rectangle frame.</summary>
        private (double x, double y, double nx, double ny) Perimeter(double u)
        {
            u = (u % 1 + 1) % 1;
            double r = CornerRadius;
            double sw = Math.Max(0, _w - 2 * r), sh = Math.Max(0, _h - 2 * r);
            double arc = Math.PI * r / 2;
            double d = u * (2 * (sw + sh) + 4 * arc);

            if (d < sw) return (r + d, 0, 0, 1);                                   // top
            d -= sw;
            if (d < arc) { double a = -Math.PI / 2 + d / r; return (_w - r + r * Math.Cos(a), r + r * Math.Sin(a), -Math.Cos(a), -Math.Sin(a)); }
            d -= arc;
            if (d < sh) return (_w, r + d, -1, 0);                                 // right
            d -= sh;
            if (d < arc) { double a = d / r; return (_w - r + r * Math.Cos(a), _h - r + r * Math.Sin(a), -Math.Cos(a), -Math.Sin(a)); }
            d -= arc;
            if (d < sw) return (_w - r - d, _h, 0, -1);                            // bottom
            d -= sw;
            if (d < arc) { double a = Math.PI / 2 + d / r; return (r + r * Math.Cos(a), _h - r + r * Math.Sin(a), -Math.Cos(a), -Math.Sin(a)); }
            d -= arc;
            if (d < sh) return (0, _h - r - d, 1, 0);                              // left
            d -= sh;
            { double a = Math.PI + d / r; return (r + r * Math.Cos(a), r + r * Math.Sin(a), -Math.Cos(a), -Math.Sin(a)); }
        }

        /// <summary>Brightness of a sweeping head at u: a hot core with a tail trailing behind it.</summary>
        private static double Sweep(double u, double head, double width)
        {
            double behind = ((head - u) % 1 + 1) % 1;
            double gap = Math.Min(behind, 1 - behind);
            double core = Math.Exp(-Math.Pow(gap / (width * 0.42), 2));
            double tail = Math.Exp(-behind / (width * 2.4));
            return Math.Min(1.4, core + 0.6 * tail);
        }

        private void OnRender(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = Math.Min(0.05, (now - _last).TotalSeconds); _last = now;
            double t = (now - _t0).TotalSeconds;
            if (dt <= 0) return;

            bool isStatic = _style == "static", isDynamic = _style == "dynamic";
            bool isAurora = _style == "aurora", isRays = _style == "rays", isTunnel = _style == "tunnel";
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
                    if (isTunnel) _ringPhase += 1.0 / RingCount;   // tunnel: a new ring is born on the beat
                }
                target = Math.Max(_audio.Bass * (isDynamic ? 0.8 : 0.55), _kick);
            }
            else
            {
                target = 0.18 + 0.12 * Math.Sin(t * 1.1);     // no audio: a calm breathing so it still feels alive
            }
            _kick = Math.Max(0, _kick - dt * (isDynamic ? 5.5 : 4.2));
            _bass += (target - _bass) * Math.Min(1, dt * (target > _bass ? 30 : (isDynamic ? 11 : 7)));

            if (!isStatic)
            {
                _flow += dt * (0.012 + _speed * (isDynamic ? 0.16 : 0.09));
                _hue += dt * (0.05 + _speed * 0.55);
                if (isTunnel) _ringPhase += dt * (0.08 + _speed * 0.35);
            }
            double hueNow = isStatic ? 0 : _hue + _hueStep;

            // Stay out of the way: when the cursor goes near the screen edges or the taskbar (using other apps,
            // opening the taskbar, resizing windows) fade the light down to a faint minimum so everything stays easy to see.
            double dimTarget = 1;
            if (GetCursorPos(out var cp))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                double mx = cp.X / dpi.DpiScaleX - Left, my = cp.Y / dpi.DpiScaleY - Top;
                double edge = Math.Min(Math.Min(mx, _w - mx), Math.Min(my, _h - my));
                double zone = Math.Max(90, 0.16 * Math.Min(_w, _h));
                if (edge < zone) dimTarget = 0.15 + 0.85 * Math.Max(0, edge) / zone;
            }
            _dim += (dimTarget - _dim) * Math.Min(1, dt * (dimTarget < _dim ? 8 : 2.5));

            double baseSize = Math.Min(_w, _h);
            double reach = (0.07 + _reach * 0.27) * baseSize;

            // gradient softness: 0 = solid colour that only fades at the very end, 1 = long smooth fade
            double plateau = 0.6 * (1 - _grad);
            double[] fade = new double[6];
            for (int k = 0; k < 6; k++)
            {
                double x = k * 0.2, q = Math.Clamp((x - plateau) / (1 - plateau), 0, 1);
                fade[k] = Math.Pow(1 - q * q * (3 - 2 * q), 1.5);
            }

            bool blobs = !isAurora && !isRays && !isTunnel;
            ShowOnly(blobs, isAurora || isRays, isTunnel);

            if (blobs) DrawBand(t, hueNow, react, isStatic, isDynamic, reach, fade);
            else if (isAurora) DrawBars(t, hueNow, react, reach, false);
            else if (isRays) DrawBars(t, hueNow, react, reach, true);
            else DrawRings(hueNow, react, reach);
        }

        private void ShowOnly(bool blobs, bool bars, bool rings)
        {
            var b = blobs ? Visibility.Visible : Visibility.Collapsed;
            foreach (var x in _blobs) if (x.Shape.Visibility != b) x.Shape.Visibility = b;
            var r = bars ? Visibility.Visible : Visibility.Collapsed;
            foreach (var x in _bars) { if (x.Shape.Visibility != r) x.Shape.Visibility = r; if (x.Halo.Visibility != r) x.Halo.Visibility = r; }
            var g = rings ? Visibility.Visible : Visibility.Collapsed;
            foreach (var x in _rings) foreach (var s in x.Shapes) if (s.Visibility != g) s.Visibility = g;
        }

        // ---------- ambient / static / dynamic: a glowing band around the rounded frame ----------
        private void DrawBand(double t, double hueNow, double react, bool isStatic, bool isDynamic, double reach, double[] fade)
        {
            // Ambient: two bright heads sweep around the frame leaving a soft trail, over a dim even base glow.
            double head1 = _flow * 2.1, head2 = _flow * 1.37 + 0.47;
            double width = 0.06 + 0.05 * (1 - _speed);
            double flare = 1 + react * 0.9 * _bass;

            double alpha = isStatic ? 0.2 + 0.36 * _bright
                : isDynamic ? Math.Min(0.8, (0.2 + 0.34 * _bright) * (0.5 + react * 1.5 * _bass))
                : Math.Min(0.72, (0.22 + 0.34 * _bright));
            alpha *= _dim;

            for (int i = 0; i < _blobs.Count; i++)
            {
                var b = _blobs[i];
                double u = b.U + (isStatic ? 0 : Math.Sin(t * 0.3 + b.Wobble) * 0.004);
                var p = Perimeter(u);

                double lvl, hot;
                if (isStatic) { lvl = 1; hot = 0; }
                else if (isDynamic)
                {
                    lvl = 0.55 + 0.9 * _bass;
                    hot = 0.35 * _bass;
                }
                else
                {
                    double s = Math.Max(Sweep(u, head1, width), Sweep(u, head2 % 1, width * 0.8)) * flare;
                    lvl = 0.34 + 1.0 * s;          // dim base all the way round + the bright sweeping head
                    hot = Math.Min(0.75, 0.9 * Math.Max(0, s - 0.45));
                }

                double wave = isStatic ? 1 : 1 + 0.12 * Math.Sin(t * 0.55 + b.Wobble);
                double r = reach * b.Size * wave * (0.52 + 0.62 * Math.Min(1.3, lvl));
                double inset = r * 0.34;
                double cx = p.x - p.nx * inset, cy = p.y - p.ny * inset;
                var c = Hot(PaletteAt(b.U * 1.6 + (isStatic ? 0 : hueNow)), hot);

                b.Shape.Width = r * 2; b.Shape.Height = r * 2;
                Canvas.SetLeft(b.Shape, cx - r); Canvas.SetTop(b.Shape, cy - r);
                double a = Math.Min(1, alpha * Math.Min(1.25, lvl));
                var st = b.Brush.GradientStops;
                for (int k = 0; k < 6; k++)
                    st[k].Color = Color.FromArgb((byte)(255 * Math.Min(1, a * fade[k])), c.R, c.G, c.B);
                st[5].Color = Color.FromArgb(0, c.R, c.G, c.B);
            }
        }

        // ---------- aurora curtain / rays ----------
        private void DrawBars(double t, double hueNow, double react, double reach, bool rays)
        {
            double alpha = Math.Min(0.8, (rays ? 0.3 : 0.26) + 0.42 * _bright) * _dim;
            double pump = 1 + react * (rays ? 1.1 : 0.85) * _bass;
            int per = _bars.Count / 4;

            for (int i = 0; i < _bars.Count; i++)
            {
                var b = _bars[i];
                int edge = Math.Min(3, i / per);                 // 0 top, 1 right, 2 bottom, 3 left
                int k = i % per;
                double span = (edge == 0 || edge == 2) ? _w : _h;
                double slot = span / per;
                double pos = slot * (k + 0.5) + (b.Seed - 0.5) * slot * 0.6;

                double thick = rays ? Math.Max(2, slot * 0.085) : Math.Max(4, slot * (0.26 + 0.24 * b.Seed));
                double wob = Math.Sin(t * (rays ? 1.1 : 0.5) * (0.6 + b.Seed) + b.Phase * 9.4);
                double len, headA;
                if (rays)
                {
                    // streaks shoot inwards, fade out, then start again — faster and brighter on the beat
                    double travel = (t * (0.35 + _speed * 1.5) * (0.6 + b.Seed * 0.9) + b.Phase) % 1;
                    len = reach * (0.3 + 1.0 * travel) * pump;
                    headA = Math.Sin(Math.PI * Math.Min(1, travel * 1.15));
                }
                else
                {
                    len = reach * (0.3 + 0.4 * b.Seed + 0.25 * wob) * pump;    // curtain bars breathe at their own pace
                    headA = 0.72 + 0.28 * wob;
                }
                len = Math.Max(6, len);

                var c = PaletteAt(b.Phase * 2.4 + hueNow + edge * 0.35);
                var tip = Hot(c, 0.6);
                double a = Math.Min(1, alpha * headA);
                PaintBar(b.Brush, c, tip, a, rays);
                PaintBar(b.HaloBrush, c, c, a * 0.4, rays);
                Place(b.Shape, b.Brush, edge, pos, thick, len);
                Place(b.Halo, b.HaloBrush, edge, pos, thick * (rays ? 5.5 : 2.4), len * (rays ? 0.95 : 1.05));
            }
        }

        private static void PaintBar(LinearGradientBrush brush, Color c, Color tip, double a, bool rays)
        {
            byte A(double f) => (byte)(255 * Math.Clamp(a * f, 0, 1));
            var st = brush.GradientStops;
            // curtain: brightest at the edge, fading inwards. rays: dark at the edge with a hot tip at the inner end.
            if (rays)
            {
                st[0].Color = Color.FromArgb(0, c.R, c.G, c.B);
                st[1].Color = Color.FromArgb(A(0.22), c.R, c.G, c.B);
                st[2].Color = Color.FromArgb(A(0.8), c.R, c.G, c.B);
                st[3].Color = Color.FromArgb(A(1), tip.R, tip.G, tip.B);
            }
            else
            {
                st[0].Color = Color.FromArgb(A(1), c.R, c.G, c.B);
                st[1].Color = Color.FromArgb(A(0.55), c.R, c.G, c.B);
                st[2].Color = Color.FromArgb(A(0.15), c.R, c.G, c.B);
                st[3].Color = Color.FromArgb(0, c.R, c.G, c.B);
            }
        }

        /// <summary>Lay a bar on one edge, standing at <paramref name="pos"/> and reaching <paramref name="len"/> inwards.</summary>
        private void Place(Rectangle rc, LinearGradientBrush brush, int edge, double pos, double thick, double len)
        {
            rc.RadiusX = rc.RadiusY = thick / 2;
            switch (edge)
            {
                case 0:                                           // top, hanging down
                    rc.Width = thick; rc.Height = len;
                    Canvas.SetLeft(rc, pos - thick / 2); Canvas.SetTop(rc, 0);
                    brush.StartPoint = new Point(0, 0); brush.EndPoint = new Point(0, 1);
                    break;
                case 1:                                           // right, reaching left
                    rc.Width = len; rc.Height = thick;
                    Canvas.SetLeft(rc, _w - len); Canvas.SetTop(rc, pos - thick / 2);
                    brush.StartPoint = new Point(1, 0); brush.EndPoint = new Point(0, 0);
                    break;
                case 2:                                           // bottom, rising up
                    rc.Width = thick; rc.Height = len;
                    Canvas.SetLeft(rc, pos - thick / 2); Canvas.SetTop(rc, _h - len);
                    brush.StartPoint = new Point(0, 1); brush.EndPoint = new Point(0, 0);
                    break;
                default:                                          // left, reaching right
                    rc.Width = len; rc.Height = thick;
                    Canvas.SetLeft(rc, 0); Canvas.SetTop(rc, pos - thick / 2);
                    brush.StartPoint = new Point(0, 0); brush.EndPoint = new Point(1, 0);
                    break;
            }
        }

        // ---------- tunnel: neon rings receding inwards ----------
        private void DrawRings(double hueNow, double react, double reach)
        {
            double alpha = Math.Min(0.75, 0.18 + 0.45 * _bright) * _dim;
            double pump = 1 + react * 0.5 * _bass;

            for (int i = 0; i < _rings.Count; i++)
            {
                var ring = _rings[i];
                double p = ((_ringPhase + i / (double)RingCount) % 1 + 1) % 1;   // 0 = at the edge, 1 = deepest in
                double inset = reach * p * 1.15;
                double w = _w - 2 * inset, h = _h - 2 * inset;
                if (w <= 2 || h <= 2) { foreach (var s in ring.Shapes) s.Visibility = Visibility.Collapsed; continue; }

                double near = 1 - p;                                             // rings dim as they recede
                double a = alpha * Math.Pow(near, 2.2) * (0.45 + 0.75 * pump * near);
                var c = PaletteAt(i * 0.6 + hueNow);
                double thick = Math.Max(1.2, reach * 0.022 * (0.5 + near));

                for (int k = 0; k < 3; k++)
                {
                    var s = ring.Shapes[k];
                    if (s.Visibility != Visibility.Visible) s.Visibility = Visibility.Visible;
                    // k = 0 wide soft halo, 1 mid, 2 thin hot core
                    double mul = k == 0 ? 7 : k == 1 ? 2.6 : 1;
                    double fa = k == 0 ? 0.12 : k == 1 ? 0.33 : 1;
                    var cc = k == 2 ? Hot(c, 0.45) : c;
                    s.StrokeThickness = thick * mul;
                    s.Width = w; s.Height = h;
                    s.RadiusX = s.RadiusY = Math.Max(0, CornerRadius - inset * 0.6);
                    Canvas.SetLeft(s, inset); Canvas.SetTop(s, inset);
                    ring.Brushes[k].Color = Color.FromArgb((byte)(255 * Math.Clamp(a * fa, 0, 1)), cc.R, cc.G, cc.B);
                }
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
