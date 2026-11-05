using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nightverse.Services
{
    public sealed class LyricsResult
    {
        [JsonPropertyName("syncedLyrics")] public string? Synced { get; set; }
        [JsonPropertyName("plainLyrics")] public string? Plain { get; set; }
        [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("trackName")] public string? TrackName { get; set; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;
        public bool NotFound { get; set; }
    }

    /// <summary>
    /// Fetches time-synced lyrics from LRCLIB (https://lrclib.net) — free, no API key.
    /// Lyrics are never bundled with the app; they are fetched live and cached locally.
    /// </summary>
    public sealed class LyricsService
    {
        private static readonly HttpClient Http = CreateClient();
        private readonly ConcurrentDictionary<string, LyricsResult> _memory = new();
        private readonly string _cacheDir;

        public LyricsService()
        {
            _cacheDir = Path.Combine(AppSettings.DataDir, "lyrics-cache");
            Directory.CreateDirectory(_cacheDir);
        }

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { BaseAddress = new Uri("https://lrclib.net/"), Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Nightverse/1.0 (+https://github.com/jay-m007/nightverse)");
            return c;
        }

        public async Task<LyricsResult> GetAsync(TrackInfo t, CancellationToken ct)
        {
            var key = Hash($"{t.Artist}|{t.Title}|{t.Album}".ToLowerInvariant());
            if (_memory.TryGetValue(key, out var mem)) return mem;
            var file = Path.Combine(_cacheDir, key + ".json");
            try
            {
                if (File.Exists(file))
                {
                    var cached = JsonSerializer.Deserialize<LyricsResult>(await File.ReadAllTextAsync(file, ct));
                    // keep hits forever, retry misses after a day (someone may have added them)
                    if (cached != null && (!cached.NotFound || DateTime.UtcNow - cached.CachedAt < TimeSpan.FromDays(1)))
                        return _memory[key] = cached;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            var result = await FetchAsync(t, ct) ?? new LyricsResult { NotFound = true };
            _memory[key] = result;
            try { await File.WriteAllTextAsync(file, JsonSerializer.Serialize(result), ct); } catch (OperationCanceledException) { throw; } catch { }
            return result;
        }

        private async Task<LyricsResult?> FetchAsync(TrackInfo t, CancellationToken ct)
        {
            var title = CleanTitle(t.Title);
            var artist = t.Artist;
            var mainArtist = Regex.Split(artist, @"\s*(?:,|&| x | feat\.?| ft\.?| and )\s*", RegexOptions.IgnoreCase)[0];
            var secs = (int)Math.Round(t.Duration.TotalSeconds);

            // 1) exact match (best when album + duration are known)
            if (secs > 0)
            {
                var exact = await GetJsonAsync<LyricsResult>(
                    $"api/get?track_name={E(title)}&artist_name={E(artist)}&album_name={E(t.Album)}&duration={secs}", ct);
                if (Good(exact)) return exact;
            }

            // 2) search, prefer synced lyrics with a close duration
            foreach (var q in new[] { $"api/search?track_name={E(title)}&artist_name={E(mainArtist)}", $"api/search?q={E(title + " " + mainArtist)}" })
            {
                var list = await GetJsonAsync<LyricsResult[]>(q, ct);
                if (list == null || list.Length == 0) continue;
                var pick = list
                    .Where(r => r.Instrumental || !string.IsNullOrWhiteSpace(r.Synced) || !string.IsNullOrWhiteSpace(r.Plain))
                    .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.Synced))
                    .ThenBy(r => secs > 0 ? Math.Abs(r.Duration - secs) : 0)
                    .FirstOrDefault(r => secs <= 0 || Math.Abs(r.Duration - secs) <= 8);
                if (pick != null) return pick;
            }
            return null;
        }

        private static bool Good(LyricsResult? r) => r != null && (r.Instrumental || !string.IsNullOrWhiteSpace(r.Synced) || !string.IsNullOrWhiteSpace(r.Plain));

        private static async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct) where T : class
        {
            try
            {
                using var res = await Http.GetAsync(url, ct);
                if (res.StatusCode == HttpStatusCode.NotFound || !res.IsSuccessStatusCode) return null;
                var json = await res.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return null; }
        }

        /// <summary>"Song (feat. X) - Remastered 2011" → "Song"</summary>
        public static string CleanTitle(string title)
        {
            var t = Regex.Replace(title, @"\s*[\(\[](?:feat|ft|with|prod|official|lyric|audio|video|visuali[sz]er|remaster|explicit|from)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+-\s+(?:.*remaster.*|live.*|radio edit|single version|mono|stereo)$", "", RegexOptions.IgnoreCase);
            return t.Trim();
        }

        private static string E(string s) => Uri.EscapeDataString(s ?? "");

        private static string Hash(string s)
        {
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
            return Convert.ToHexString(bytes)[..20].ToLowerInvariant();
        }
    }
}
