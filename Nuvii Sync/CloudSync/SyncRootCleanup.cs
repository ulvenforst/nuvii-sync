using System;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using Windows.Storage.Provider;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Utility class for cleaning up orphaned sync root registrations.
    /// </summary>
    public static class SyncRootCleanup
    {
        private const string STORAGE_PROVIDER_ID = "NuviiSync";

        /// <summary>
        /// Forces cleanup by running PowerShell to remove registry entries and restart Explorer.
        /// </summary>
        public static void ForceCleanup()
        {
            Trace.WriteLine("=== Force Cleanup via PowerShell ===");

            var script = @"
$basePath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace'
Get-ChildItem $basePath -ErrorAction SilentlyContinue | ForEach-Object {
    $name = (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).'(default)'
    if ($name -like '*Nuvii*') {
        Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue
    }
}

$syncRootPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager'
Get-ChildItem $syncRootPath -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.PSChildName -like '*Nuvii*' -or $_.PSChildName -like '*NuviiSync*') {
        Remove-Item $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Process explorer
";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi);
                Trace.WriteLine("  Cleanup script executed");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"  Cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if there's any Nuvii sync root registered.
        /// </summary>
        public static bool HasOrphanedRegistration()
        {
            try
            {
                var syncRoots = StorageProviderSyncRootManager.GetCurrentSyncRoots();

                foreach (var root in syncRoots)
                {
                    if (root.Id.StartsWith(STORAGE_PROVIDER_ID + "!", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }

            // Also check registry
            try
            {
                using var syncRootKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager");
                if (syncRootKey != null)
                {
                    foreach (var subKeyName in syncRootKey.GetSubKeyNames())
                    {
                        if (subKeyName.Contains("Nuvii", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Gets the sync root ID for the current user.
        /// </summary>
        public static string GetSyncRootId()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var userSid = identity.User?.Value ?? throw new InvalidOperationException("Could not get current user SID");
            return $"{STORAGE_PROVIDER_ID}!{userSid}!NuviiAccount";
        }
    }
}
