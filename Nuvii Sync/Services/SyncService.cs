using System;
using System.Threading.Tasks;
using Nuvii_Sync.CloudSync;

namespace Nuvii_Sync.Services
{
    /// <summary>
    /// Singleton service that manages the CloudSyncProvider lifecycle.
    /// This ensures only one sync provider instance exists across the app.
    /// </summary>
    public sealed class SyncService : IDisposable
    {
        private static readonly Lazy<SyncService> _instance = new(() => new SyncService());
        public static SyncService Instance => _instance.Value;

        private CloudSyncProvider? _syncProvider;
        private bool _disposed;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<SyncActivityEventArgs>? ActivityOccurred;

        public bool IsRunning => _syncProvider?.IsRunning ?? false;
        public bool IsInitialized => _syncProvider != null;

        private SyncService()
        {
        }

        /// <summary>
        /// Starts the sync provider with the specified folders.
        /// </summary>
        public async Task<bool> StartAsync(string serverFolder, string clientFolder)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SyncService));

            // Create provider if not exists
            if (_syncProvider == null)
            {
                _syncProvider = new CloudSyncProvider();
                _syncProvider.StatusChanged += OnProviderStatusChanged;
                _syncProvider.ActivityOccurred += OnProviderActivityOccurred;
            }

            // If already running with same folders, just return success
            if (_syncProvider.IsRunning)
            {
                return true;
            }

            var success = await _syncProvider.StartAsync(serverFolder, clientFolder);

            if (success)
            {
                // Save paths on successful start
                SettingsService.SavePaths(serverFolder, clientFolder);
            }

            return success;
        }

        /// <summary>
        /// Stops the sync provider without unregistering.
        /// </summary>
        public async Task StopAsync()
        {
            if (_syncProvider != null && _syncProvider.IsRunning)
            {
                await _syncProvider.StopAsync(unregister: false);
            }
        }

        /// <summary>
        /// Forces cleanup of all sync roots.
        /// </summary>
        public async Task ForceCleanupAsync()
        {
            await CloudSyncProvider.ForceCleanupAsync();
        }

        /// <summary>
        /// Checks if there's an orphaned registration from a previous session.
        /// </summary>
        public bool HasOrphanedRegistration()
        {
            return SyncRootCleanup.HasOrphanedRegistration();
        }

        private void OnProviderStatusChanged(object? sender, string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        private void OnProviderActivityOccurred(object? sender, SyncActivityEventArgs e)
        {
            ActivityOccurred?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_syncProvider != null)
            {
                _syncProvider.StatusChanged -= OnProviderStatusChanged;
                _syncProvider.ActivityOccurred -= OnProviderActivityOccurred;
                _syncProvider.Dispose();
                _syncProvider = null;
            }
        }
    }
}
