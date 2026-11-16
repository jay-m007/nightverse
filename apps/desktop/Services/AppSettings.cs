using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;

namespace Nightverse.Services
{
    /// <summary>Ambient lighting options, editable from the Lights panel.</summary>
    public sealed class LightingSettings
    {
        /// <summary>"custom" = your colours, "album" = colour taken from the cover art.</summary>
        public string Mode { get; set; } = "custom";
        /// <summary>ambient | static | dynamic | aurora | rays | tunnel — see GlowWindow.</summary>
        public string Style { get; set; } = "ambient";
        public List<string> Colors { get; set; } = new() { "#ff2a55", "#35ff8a", "#2d7bff" };
        public double React { get; set; } = 0.7;       // how hard the light kicks on the beat
        public double Speed { get; set; } = 0.35;      // how fast it flows / changes colour
        public double Reach { get; set; } = 0.45;      // screen coverage: how far the light spreads inwards
        public double Gradient { get; set; } = 0.85;   // 0 = solid colour with a short fade, 1 = long, smooth gradient
        public double Brightness { get; set; } = 0.8;
    }

    public sealed class AppSettings
    {
        public string Theme { get; set; } = "kinetic";
        public bool ShowClock { get; set; } = true;
        public bool GlowEnabled { get; set; } = true;
        public double GlowIntensity { get; set; } = 0.8;
        public bool AlwaysOnTop { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool HasSeenTrayTip { get; set; } = false;
        public LightingSettings Lighting { get; set; } = new();

        public static string DataDir
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nightverse");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string FilePath => Path.Combine(DataDir, "settings.json");
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                    loaded.Lighting ??= new LightingSettings();
                    if (loaded.Lighting.Colors == null || loaded.Lighting.Colors.Count == 0) loaded.Lighting.Colors = new() { "#ff2a55", "#35ff8a", "#2d7bff" };
                    return loaded;
                }
            }
            catch { /* corrupt file: start fresh */ }
            return new AppSettings();
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); } catch { }
        }
    }
}
