using System;
using System.IO;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Watches for changes in the server folder and notifies the sync provider.
    /// 
    /// This is a DEMO/LOCAL implementation. In production, this will be replaced
    /// by SignalR events from the backend. The interface (events) remains the same,
    /// so the rest of the code doesn't need to change.
    /// 
    /// Events raised:
    /// - FileCreated: New file/folder on server → create placeholder
    /// - FileDeleted: File/folder deleted on server → delete placeholder
    /// - FileRenamed: File/folder renamed on server → rename placeholder
    /// </summary>
    public sealed class ServerWatcher : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private readonly string _serverPath;
        private bool _disposed;

        /// <summary>
        /// Raised when a file or folder is created on the server.
        /// </summary>
        public event EventHandler<FileSystemEventArgs>? FileCreated;

        /// <summary>
        /// Raised when a file or folder is deleted on the server.
        /// </summary>
        public event EventHandler<FileSystemEventArgs>? FileDeleted;

        /// <summary>
        /// Raised when a file or folder is renamed on the server.
        /// </summary>
        public event EventHandler<RenamedEventArgs>? FileRenamed;

        public ServerWatcher(string serverPath)
        {
            _serverPath = serverPath;
        }

        public void Start()
        {
            if (_watcher != null) return;

            _watcher = new FileSystemWatcher(_serverPath)
            {
                // Watch for file/folder creation, deletion, and renaming
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;

            Trace.WriteLine($"[ServerWatcher] Started: {_serverPath}");
        }

        public void Stop()
        {
            if (_watcher == null) return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnCreated;
            _watcher.Deleted -= OnDeleted;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;

            Trace.WriteLine($"[ServerWatcher] Stopped: {_serverPath}");
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            Trace.WriteLine($"[ServerWatcher] Created: {e.FullPath}");
            FileCreated?.Invoke(this, e);
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            Trace.WriteLine($"[ServerWatcher] Deleted: {e.FullPath}");
            FileDeleted?.Invoke(this, e);
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            Trace.WriteLine($"[ServerWatcher] Renamed: {e.OldFullPath} -> {e.FullPath}");
            FileRenamed?.Invoke(this, e);
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Trace.WriteLine($"[ServerWatcher] Error: {e.GetException().Message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
