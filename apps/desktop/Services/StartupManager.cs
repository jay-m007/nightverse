using System;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Nightverse.Services
{
    /// <summary>Start-with-Windows: StartupTask when installed from the Store (MSIX), registry Run key otherwise.</summary>
    public static class StartupManager
    {
        private const string TaskId = "NightverseStartup";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsPackaged
        {
            get
            {
                try { return Windows.ApplicationModel.Package.Current != null; }
                catch { return false; }
            }
        }

        public static async Task<bool> SetEnabledAsync(bool enabled)
        {
            try
            {
                if (IsPackaged)
                {
                    var task = await Windows.ApplicationModel.StartupTask.GetAsync(TaskId);
                    if (!enabled) { task.Disable(); return false; }
                    var state = await task.RequestEnableAsync();
                    return state == Windows.ApplicationModel.StartupTaskState.Enabled;
                }
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue("Nightverse", $"\"{Environment.ProcessPath}\" --minimized");
                else key.DeleteValue("Nightverse", throwOnMissingValue: false);
                return enabled;
            }
            catch { return false; }
        }
    }
}
