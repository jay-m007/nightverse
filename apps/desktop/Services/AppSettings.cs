using System;
using System.IO;
using System.Text.Json;

namespace Nightverse.Services
{
    public sealed class AppSettings
    {
        public string Theme { get; set; } = "kinetic";
        public bool ShowClock { get; set; } = true;
        public bool GlowEnabled { get; set; } = false;
        public double GlowIntensity { get; set; } = 0.8;
        public bool AlwaysOnTop { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool HasSeenTrayTip { get; set; } = false;

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
