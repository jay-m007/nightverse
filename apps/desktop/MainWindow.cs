using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Nightverse
{
    /// <summary>The lyrics window: a WebView2 hosting the shared web renderer.</summary>
    public sealed class MainWindow : Window
    {
        private readonly WebView2 _web = new();
        private bool _ready;
        private bool _fullscreen, _mini;
        private Rect _restoreBounds;
        private WindowState _restoreState;

        public bool AllowClose { get; set; }
        public bool IsMini => _mini;
        public event Action<JsonElement>? MessageReceived;
        public event Action? RendererReady;

        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public MainWindow()
        {
            Title = "Nightverse";
            Width = 1180; Height = 720; MinWidth = 320; MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(5, 5, 6));
            _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 5, 5, 6);
            Content = _web;
            Loaded += async (_, _) => await InitWebAsync();
        }

        private async Task InitWebAsync()
        {
            try
            {
                // MSIX install folders are read-only, so WebView2 data must live in LocalAppData.
                var dataDir = Path.Combine(Services.AppSettings.DataDir, "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                await _web.EnsureCoreWebView2Async(env);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show("Nightverse needs the Microsoft Edge WebView2 Runtime.\n\nIt comes with Windows 11; on Windows 10 install it from:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703",
                    "Nightverse", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var core = _web.CoreWebView2;
            var webDir = Path.Combine(AppContext.BaseDirectory, "web");
            core.SetVirtualHostNameToFolderMapping("nightverse.app", webDir, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDevToolsEnabled = System.Diagnostics.Debugger.IsAttached;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true; // open links (e.g. lrclib.net credit) in the real browser
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
            };
            core.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var msg = doc.RootElement.Clone();
                    if (msg.TryGetProperty("type", out var t) && t.GetString() == "ready")
                    {
                        _ready = true;
                        RendererReady?.Invoke();
                    }
                    else MessageReceived?.Invoke(msg);
                }
                catch { }
            };
            core.Navigate("https://nightverse.app/index.html");
        }

        public void Post(object message)
        {
            if (!_ready || _web.CoreWebView2 == null) return;
            try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json)); } catch { }
        }

        // ---------- window modes ----------
        public void ToggleFullscreen()
        {
            if (_mini) ToggleMini();
            if (!_fullscreen)
            {
                _restoreState = WindowState;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                _fullscreen = true;
            }
            else ExitFullscreen();
        }

        public void ExitFullscreen()
        {
            if (!_fullscreen) return;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _restoreState;
            _fullscreen = false;
        }

        public void ToggleMini()
        {
            if (_fullscreen) ExitFullscreen();
            if (!_mini)
            {
                _restoreBounds = new Rect(Left, Top, Width, Height);
                _restoreState = WindowState;
                WindowState = WindowState.Normal;
                Width = 340; Height = 470;
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 24; Top = wa.Bottom - Height - 24;
                Topmost = true;
                _mini = true;
            }
            else
            {
                Topmost = AlwaysOnTop;
                Left = _restoreBounds.Left; Top = _restoreBounds.Top; Width = _restoreBounds.Width; Height = _restoreBounds.Height;
                WindowState = _restoreState;
                _mini = false;
            }
        }

        public bool AlwaysOnTop { get; set; }

        public void BringToFront()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!AllowClose) { e.Cancel = true; Hide(); }
            base.OnClosing(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // dark title bar on Windows 10 2004+ / 11
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
