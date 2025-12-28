using System;
using Windows.Storage;

namespace Nuvii_Sync.Services
{
    /// <summary>
    /// Service for managing application settings persistence.
    /// </summary>
    public static class SettingsService
    {
        private const string ServerFolderKey = "ServerFolder";
        private const string ClientFolderKey = "ClientFolder";

        /// <summary>
        /// Gets the saved server folder path.
        /// </summary>
        public static string? ServerFolder
        {
            get => GetSetting(ServerFolderKey);
            set => SetSetting(ServerFolderKey, value);
        }

        /// <summary>
        /// Gets the saved client folder path (sync root).
        /// </summary>
        public static string? ClientFolder
        {
            get => GetSetting(ClientFolderKey);
            set => SetSetting(ClientFolderKey, value);
        }

        /// <summary>
        /// Gets whether settings have been configured.
        /// </summary>
        public static bool HasSavedPaths =>
            !string.IsNullOrEmpty(ServerFolder) && !string.IsNullOrEmpty(ClientFolder);

        /// <summary>
        /// Saves both folder paths.
        /// </summary>
        public static void SavePaths(string serverFolder, string clientFolder)
        {
            ServerFolder = serverFolder;
            ClientFolder = clientFolder;
        }

        /// <summary>
        /// Clears all saved settings.
        /// </summary>
        public static void ClearPaths()
        {
            ServerFolder = null;
            ClientFolder = null;
        }

        private static string? GetSetting(string key)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.TryGetValue(key, out var value))
                {
                    return value?.ToString();
                }
            }
            catch { }
            return null;
        }

        private static void SetSetting(string key, string? value)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (string.IsNullOrEmpty(value))
                {
                    localSettings.Values.Remove(key);
                }
                else
                {
                    localSettings.Values[key] = value;
                }
            }
            catch { }
        }
    }
}
