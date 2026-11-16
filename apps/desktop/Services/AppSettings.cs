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
        /// <summary>"ambient" = soft flowing light, "static" = fixed colours that stay put, "dynamic" = hits hard on every beat.</summary>
        public string Style { get; set; } = "ambient";
        public List<string> Colors { get; set; } = new() { "#ff2a55", "#35ff8a", "#2d7bff" };
        public double React { get; set; } = 0.7;       // how hard the light kicks on the beat
        public double Speed { get; set; } = 0.35;      // how fast it flows / changes colour
        public double Reach { get; set; } = 0.45;      // how far it spreads inwards
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
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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
