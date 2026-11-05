using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Nightverse.Services
{
    public sealed record TrackInfo(string Title, string Artist, string Album, TimeSpan Duration, string SourceApp)
    {
        public string Key => $"{SourceApp}|{Artist}|{Title}".ToLowerInvariant();
    }

    /// <summary>
    /// Reads "now playing" from the Windows System Media Transport Controls.
    /// Spotify desktop, Chromium/Edge tabs (YT Music, YouTube), Apple Music etc. all publish here,
    /// so no Spotify or YouTube API access is needed.
    /// </summary>
    public sealed class MediaWatcher : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private TrackInfo? _track;

        public event Action<TrackInfo?>? TrackChanged;
        public event Action<string?>? CoverChanged;           // data: URL or null
        public event Action<TimeSpan, bool>? PositionChanged; // position, playing

        public TrackInfo? CurrentTrack => _track;
        public bool IsPlaying { get; private set; }

        public async Task StartAsync()
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ = PickSessionAsync();
            _manager.SessionsChanged += (_, _) => _ = PickSessionAsync();
            await PickSessionAsync();
        }

        /// <summary>Prefer a session that is actually playing; fall back to Windows' "current" one.</summary>
        private async Task PickSessionAsync()
        {
            if (_manager == null) return;
            GlobalSystemMediaTransportControlsSession? best = null;
            try
            {
                var sessions = _manager.GetSessions();
                best = sessions.FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                       ?? _manager.GetCurrentSession()
                       ?? sessions.FirstOrDefault();
            }
            catch { }

            if (!ReferenceEquals(best, _session))
            {
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnMediaProperties;
                    _session.PlaybackInfoChanged -= OnPlaybackInfo;
                    _session.TimelinePropertiesChanged -= OnTimeline;
                }
                _session = best;
                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnMediaProperties;
                    _session.PlaybackInfoChanged += OnPlaybackInfo;
                    _session.TimelinePropertiesChanged += OnTimeline;
                }
            }
            await RefreshPropertiesAsync();
            PushPosition();
        }

        private void OnMediaProperties(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshPropertiesAsync();
        private void OnPlaybackInfo(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) { PushPosition(); _ = PickSessionAsync(); }
        private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => PushPosition();

        private async Task RefreshPropertiesAsync()
        {
            var session = _session;
            if (session == null)
            {
                if (_track != null) { _track = null; TrackChanged?.Invoke(null); }
                return;
            }
            GlobalSystemMediaTransportControlsSessionMediaProperties? props;
            try { props = await session.TryGetMediaPropertiesAsync(); }
            catch { return; }
            if (props == null || string.IsNullOrWhiteSpace(props.Title)) return;

            TimeSpan duration = TimeSpan.Zero;
            try { var tl = session.GetTimelineProperties(); duration = tl.EndTime - tl.StartTime; } catch { }

            var artist = CleanArtist(props.Artist ?? props.AlbumArtist ?? "");
            var track = new TrackInfo(props.Title.Trim(), artist, props.AlbumTitle ?? "", duration, session.SourceAppUserModelId ?? "");
            if (_track != null && _track.Key == track.Key)
            {
                // same song; duration may arrive late
                if (_track.Duration == TimeSpan.Zero && duration > TimeSpan.Zero) _track = track;
                return;
            }
            _track = track;
            TrackChanged?.Invoke(track);
            CoverChanged?.Invoke(await ReadCoverAsync(props.Thumbnail));
        }

        private static string CleanArtist(string a)
        {
            a = a.Trim();
            if (a.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)) a = a[..^8];
            return a;
        }

        private static async Task<string?> ReadCoverAsync(IRandomAccessStreamReference? thumb)
        {
            if (thumb == null) return null;
            try
            {
                using var stream = await thumb.OpenReadAsync();
                using var net = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                await net.CopyToAsync(ms);
                var bytes = ms.ToArray();
                if (bytes.Length == 0) return null;
                var mime = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png" : "image/jpeg";
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
            catch { return null; }
        }

        /// <summary>Called on events and once a second by the app.</summary>
        public void PushPosition()
        {
            var session = _session;
            if (session == null) { IsPlaying = false; return; }
            try
            {
                var info = session.GetPlaybackInfo();
                var tl = session.GetTimelineProperties();
                bool playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var pos = tl.Position;
                if (playing && tl.LastUpdatedTime.Year > 2000)
                {
                    // SMTC gives the position at LastUpdatedTime; extrapolate to now.
                    var elapsed = DateTimeOffset.Now - tl.LastUpdatedTime;
                    if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(1)) pos += elapsed;
                }
                var end = tl.EndTime - tl.StartTime;
                if (end > TimeSpan.Zero && pos > end) pos = end;
                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                IsPlaying = playing;
                PositionChanged?.Invoke(pos, playing);
            }
            catch { }
        }

        public async Task CommandAsync(string name)
        {
            var s = _session;
            if (s == null) return;
            try
            {
                switch (name)
                {
                    case "playPause": await s.TryTogglePlayPauseAsync(); break;
                    case "next": await s.TrySkipNextAsync(); break;
                    case "previous": await s.TrySkipPreviousAsync(); break;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaProperties;
                _session.PlaybackInfoChanged -= OnPlaybackInfo;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
        }
    }
}
