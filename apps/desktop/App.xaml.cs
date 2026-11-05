using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nightverse.Services;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;

namespace Nightverse
{
    public partial class App : Application
    {
        private static Mutex? _single;
        private AppSettings _settings = new();
        private readonly MediaWatcher _media = new();
        private readonly LyricsService _lyrics = new();
        private MainWindow _main = null!;
        private GlowWindow? _glow;
        private Forms.NotifyIcon? _tray;
        private DispatcherTimer? _timer;
        private CancellationTokenSource? _lyricsCts;

        // last state, replayed when the renderer (re)loads
        private object? _lastTrack, _lastLyrics;
        private string? _cover;
        private WpfColor _accent = WpfColor.FromRgb(255, 170, 60);

        private static readonly string[] Themes = { "kinetic", "constellation", "classic", "vinyl", "neon", "cyberpunk", "comic", "terminal", "synthwave", "karaoke", "aurora", "rain", "warp", "editorial" };
        private static readonly string[] ThemeLabels = { "Kinetic", "Constellation", "Classic", "Vinyl", "Neon Sign", "Cyberpunk", "Comic Pop", "Terminal", "Synthwave", "Karaoke", "Aurora", "Rainy Window", "Warp Speed", "Editorial" };

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _single = new Mutex(true, "Nightverse.SingleInstance", out bool first);
            if (!first) { Shutdown(); return; }

            _settings = AppSettings.Load();
            _main = new MainWindow { AlwaysOnTop = _settings.AlwaysOnTop, Topmost = _settings.AlwaysOnTop };
            try { _main.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico")); } catch { }
            _main.RendererReady += ReplayState;
            _main.MessageReceived += OnRendererMessage;

            BuildTray();

            bool launchedAtBoot = e.Args.Contains("--minimized") || (_settings.StartWithWindows && Environment.TickCount64 < 180_000);
            if (!launchedAtBoot) _main.Show();

            _media.TrackChanged += t => Dispatcher.InvokeAsync(() => OnTrackChanged(t));
            _media.CoverChanged += c => Dispatcher.InvokeAsync(() => { _cover = c; SendTrack(); });
            _media.PositionChanged += (pos, playing) => Dispatcher.InvokeAsync(() =>
            {
                _main.Post(new { type = "position", positionMs = (long)pos.TotalMilliseconds, playing });
                UpdateGlowVisibility(playing);
            });

            try { await _media.StartAsync(); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read Windows media sessions: " + ex.Message, "Nightverse");
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += (_, _) => _media.PushPosition();
            _timer.Start();

            if (_settings.GlowEnabled) EnsureGlow();
        }

        // ---------- now playing ----------
        private void OnTrackChanged(TrackInfo? t)
        {
            _lyricsCts?.Cancel();
            if (t == null)
            {
                _lastTrack = null; _lastLyrics = null; _cover = null;
                _main.Post(new { type = "idle" });
                UpdateTray(null);
                return;
            }
            _cover = null;
            SendTrack();
            UpdateTray(t);
            _ = LoadLyricsAsync(t);
        }

        private void SendTrack()
        {
            var t = _media.CurrentTrack;
            if (t == null) return;
            _lastTrack = new { type = "track", title = t.Title, artist = t.Artist, album = t.Album, durationMs = (long)t.Duration.TotalMilliseconds, cover = _cover };
            _main.Post(_lastTrack);
        }

        private async Task LoadLyricsAsync(TrackInfo t)
        {
            var cts = _lyricsCts = new CancellationTokenSource();
            _lastLyrics = new { type = "lyrics", status = "loading" };
            try
            {
                var r = await _lyrics.GetAsync(t, cts.Token);
                if (cts.IsCancellationRequested) return;
                _lastLyrics = new { type = "lyrics", synced = r.Synced, plain = r.Plain, instrumental = r.Instrumental, status = r.NotFound ? "notfound" : "found" };
            }
            catch (OperationCanceledException) { return; }
            catch { _lastLyrics = new { type = "lyrics", status = "notfound" }; }
            _main.Post(_lastLyrics);
            _media.PushPosition();
        }

        private void ReplayState()
        {
            _main.Post(new { type = "settings", settings = new { theme = _settings.Theme, showClock = _settings.ShowClock, mini = _main.IsMini } });
            if (_lastTrack != null) _main.Post(_lastTrack); else _main.Post(new { type = "idle" });
            if (_lastLyrics != null) _main.Post(_lastLyrics);
            _media.PushPosition();
        }

        // ---------- messages from the renderer ----------
        private void OnRendererMessage(JsonElement msg)
        {
            var type = msg.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "command":
                    HandleCommand(msg.GetProperty("name").GetString() ?? "");
                    break;
                case "settings":
                    if (msg.TryGetProperty("settings", out var s))
                    {
                        if (s.TryGetProperty("theme", out var th) && th.GetString() is string theme && Themes.Contains(theme)) _settings.Theme = theme;
                        if (s.TryGetProperty("showClock", out var sc) && (sc.ValueKind == JsonValueKind.True || sc.ValueKind == JsonValueKind.False)) _settings.ShowClock = sc.GetBoolean();
                        _settings.Save();
                        RefreshTrayChecks();
                    }
                    break;
                case "accent":
                    if (msg.TryGetProperty("color", out var c) && TryParseHex(c.GetString(), out var col))
                    {
                        _accent = col;
                        _glow?.SetColor(col);
                    }
                    break;
            }
        }

        private void HandleCommand(string name)
        {
            switch (name)
            {
                case "playPause":
                case "next":
                case "previous":
                    _ = _media.CommandAsync(name); break;
                case "toggleGlow": SetGlow(!_settings.GlowEnabled); break;
                case "toggleMini": ToggleMini(); break;
                case "fullscreen": _main.ToggleFullscreen(); break;
                case "exitFullscreen": _main.ExitFullscreen(); break;
            }
        }

        private void ToggleMini()
        {
            _main.ToggleMini();
            _main.Post(new { type = "settings", settings = new { mini = _main.IsMini, theme = _main.IsMini ? "vinyl" : _settings.Theme } });
            RefreshTrayChecks();
        }

        private void SetTheme(string theme)
        {
            _settings.Theme = theme; _settings.Save();
            _main.Post(new { type = "settings", settings = new { theme } });
            RefreshTrayChecks();
        }

        // ---------- glow ----------
        private void EnsureGlow()
        {
            if (_glow != null) return;
            _glow = new GlowWindow { Intensity = _settings.GlowIntensity };
            _glow.SetColor(_accent);
            UpdateGlowVisibility(_media.IsPlaying);
        }

        private void SetGlow(bool on)
        {
            _settings.GlowEnabled = on; _settings.Save();
            if (on) EnsureGlow();
            else if (_glow != null) _glow.FadeTo(false);
            RefreshTrayChecks();
        }

        private bool _glowShown;
        private void UpdateGlowVisibility(bool playing)
        {
            if (_glow == null) return;
            bool want = _settings.GlowEnabled && playing;
            if (want == _glowShown) return;
            _glowShown = want;
            _glow.FadeTo(want);
        }

        // ---------- tray ----------
        private Forms.ToolStripMenuItem? _nowPlayingItem, _glowItem, _miniItem, _topItem, _startupItem, _clockItem;
        private readonly System.Collections.Generic.List<Forms.ToolStripMenuItem> _themeItems = new();

        private void BuildTray()
        {
            _tray = new Forms.NotifyIcon { Text = "Nightverse", Visible = true };
            try
            {
                var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                _tray.Icon = System.IO.File.Exists(icoPath) ? new System.Drawing.Icon(icoPath) : System.Drawing.SystemIcons.Application;
            }
            catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

            var menu = new Forms.ContextMenuStrip();
            _nowPlayingItem = new Forms.ToolStripMenuItem("Nothing playing") { Enabled = false };
            menu.Items.Add(_nowPlayingItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Open Nightverse", null, (_, _) => _main.BringToFront());
            var styles = new Forms.ToolStripMenuItem("Lyric style");
            for (int i = 0; i < Themes.Length; i++)
            {
                var theme = Themes[i];
                var item = new Forms.ToolStripMenuItem(ThemeLabels[i], null, (_, _) => SetTheme(theme)) { Tag = theme };
                _themeItems.Add(item); styles.DropDownItems.Add(item);
            }
            menu.Items.Add(styles);
            _clockItem = new Forms.ToolStripMenuItem("Show clock", null, (_, _) =>
            {
                _settings.ShowClock = !_settings.ShowClock; _settings.Save();
                _main.Post(new { type = "settings", settings = new { showClock = _settings.ShowClock } });
                RefreshTrayChecks();
            });
            menu.Items.Add(_clockItem);
            _glowItem = new Forms.ToolStripMenuItem("Screen glow", null, (_, _) => SetGlow(!_settings.GlowEnabled));
            menu.Items.Add(_glowItem);
            var intensity = new Forms.ToolStripMenuItem("Glow strength");
            foreach (var (label, val) in new[] { ("Soft", 0.35), ("Medium", 0.65), ("Strong", 1.0) })
                intensity.DropDownItems.Add(label, null, (_, _) => { _settings.GlowIntensity = val; _settings.Save(); if (_glow != null) { _glow.Intensity = val; _glow.SetColor(_accent); } });
            menu.Items.Add(intensity);
            _miniItem = new Forms.ToolStripMenuItem("Mini vinyl player", null, (_, _) => { _main.BringToFront(); ToggleMini(); });
            menu.Items.Add(_miniItem);
            _topItem = new Forms.ToolStripMenuItem("Always on top", null, (_, _) =>
            {
                _settings.AlwaysOnTop = !_settings.AlwaysOnTop; _settings.Save();
                _main.AlwaysOnTop = _settings.AlwaysOnTop;
                if (!_main.IsMini) _main.Topmost = _settings.AlwaysOnTop;
                RefreshTrayChecks();
            });
            menu.Items.Add(_topItem);
            _startupItem = new Forms.ToolStripMenuItem("Start with Windows", null, async (_, _) =>
            {
                _settings.StartWithWindows = await StartupManager.SetEnabledAsync(!_settings.StartWithWindows);
                _settings.Save(); RefreshTrayChecks();
            });
            menu.Items.Add(_startupItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("About / GitHub", null, (_, _) => OpenUrl("https://github.com/jay-m007/nightverse"));
            menu.Items.Add("Quit", null, (_, _) => Quit());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) _main.BringToFront(); };
            RefreshTrayChecks();

            _main.IsVisibleChanged += (_, _) =>
            {
                if (!_main.IsVisible && !_settings.HasSeenTrayTip && _tray != null)
                {
                    _settings.HasSeenTrayTip = true; _settings.Save();
                    _tray.ShowBalloonTip(4000, "Nightverse is still running", "It keeps the glow and lyrics ready here in the tray. Right-click for options.", Forms.ToolTipIcon.Info);
                }
            };
        }

        private void RefreshTrayChecks()
        {
            foreach (var i in _themeItems) i.Checked = (string?)i.Tag == _settings.Theme;
            if (_glowItem != null) _glowItem.Checked = _settings.GlowEnabled;
            if (_miniItem != null) _miniItem.Checked = _main.IsMini;
            if (_topItem != null) _topItem.Checked = _settings.AlwaysOnTop;
            if (_startupItem != null) _startupItem.Checked = _settings.StartWithWindows;
            if (_clockItem != null) _clockItem.Checked = _settings.ShowClock;
        }

        private void UpdateTray(TrackInfo? t)
        {
            if (_tray == null || _nowPlayingItem == null) return;
            var text = t == null ? "Nothing playing" : $"{t.Title} — {t.Artist}";
            _nowPlayingItem.Text = text.Length > 60 ? text[..57] + "…" : text;
            var tip = "Nightverse" + (t == null ? "" : $"\n{t.Title}");
            _tray.Text = tip.Length > 63 ? tip[..63] : tip;
        }

        private static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private static bool TryParseHex(string? hex, out WpfColor c)
        {
            c = default;
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            try
            {
                c = WpfColor.FromRgb(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
                return true;
            }
            catch { return false; }
        }

        private void Quit()
        {
            _timer?.Stop();
            _media.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _main.AllowClose = true;
            _main.Close();
            _glow?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            base.OnExit(e);
        }
    }
}
