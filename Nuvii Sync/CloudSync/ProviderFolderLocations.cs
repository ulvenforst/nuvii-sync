using System;
using System.IO;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Manages the folder locations for the cloud sync provider.
    /// ServerFolder represents the "cloud" source, ClientFolder represents the sync root.
    /// </summary>
    public static class ProviderFolderLocations
    {
        private static string? _serverFolder;
        private static string? _clientFolder;

        /// <summary>
        /// Gets the server folder path (cloud source).
        /// </summary>
        public static string ServerFolder => _serverFolder ?? throw new InvalidOperationException("Server folder not initialized");

        /// <summary>
        /// Gets the client folder path (sync root).
        /// </summary>
        public static string ClientFolder => _clientFolder ?? throw new InvalidOperationException("Client folder not initialized");

        /// <summary>
        /// Gets whether the folder locations have been initialized.
        /// </summary>
        public static bool IsInitialized => !string.IsNullOrEmpty(_serverFolder) && !string.IsNullOrEmpty(_clientFolder);

        /// <summary>
        /// Initializes the provider folder locations.
        /// </summary>
        /// <param name="serverFolder">Path to the server folder (cloud source).</param>
        /// <param name="clientFolder">Path to the client folder (sync root).</param>
        /// <returns>True if initialization was successful.</returns>
        public static bool Initialize(string serverFolder, string clientFolder)
        {
            if (string.IsNullOrWhiteSpace(serverFolder) || string.IsNullOrWhiteSpace(clientFolder))
            {
                return false;
            }

            // Create directories if they don't exist
            try
            {
                if (!Directory.Exists(serverFolder))
                {
                    Directory.CreateDirectory(serverFolder);
                }

                if (!Directory.Exists(clientFolder))
                {
                    Directory.CreateDirectory(clientFolder);
                }

                _serverFolder = Path.GetFullPath(serverFolder);
                _clientFolder = Path.GetFullPath(clientFolder);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize folder locations: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resets the folder locations.
        /// </summary>
        public static void Reset()
        {
            _serverFolder = null;
            _clientFolder = null;
        }

        /// <summary>
        /// Gets the relative path from the client folder to a full path.
        /// </summary>
        public static string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(_clientFolder))
                return fullPath;

            return Path.GetRelativePath(_clientFolder, fullPath);
        }

        /// <summary>
        /// Converts a client path to the corresponding server path.
        /// </summary>
        public static string ClientToServerPath(string clientPath)
        {
            if (string.IsNullOrEmpty(_serverFolder) || string.IsNullOrEmpty(_clientFolder))
                throw new InvalidOperationException("Folder locations not initialized");

            var relativePath = GetRelativePath(clientPath);
            return Path.Combine(_serverFolder, relativePath);
        }

        /// <summary>
        /// Converts a server path to the corresponding client path.
        /// </summary>
        public static string ServerToClientPath(string serverPath)
        {
            if (string.IsNullOrEmpty(_serverFolder) || string.IsNullOrEmpty(_clientFolder))
                throw new InvalidOperationException("Folder locations not initialized");

            var relativePath = Path.GetRelativePath(_serverFolder, serverPath);
            return Path.Combine(_clientFolder, relativePath);
        }
    }
}
