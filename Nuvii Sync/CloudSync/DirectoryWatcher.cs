using System;
using System.IO;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Watches for changes in a directory.
    /// Based on CloudMirror sample DirectoryWatcher.cpp
    /// 
    /// CloudMirror ONLY watches for attribute changes (FILE_NOTIFY_CHANGE_ATTRIBUTES)
    /// to detect when user pins/unpins files via "Always keep on this device" or
    /// "Free up space" in Explorer's context menu.
    /// </summary>
    public sealed class DirectoryWatcher : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private readonly string _path;
        private bool _disposed;

        /// <summary>
        /// Event raised when a file's attributes change (pin/unpin).
        /// </summary>
        public event EventHandler<FileSystemEventArgs>? Changed;

        public DirectoryWatcher(string path)
        {
            _path = path;
        }

        public void Start()
        {
            if (_watcher != null) return;

            _watcher = new FileSystemWatcher(_path)
            {
                // CloudMirror: FILE_NOTIFY_CHANGE_ATTRIBUTES only
                NotifyFilter = NotifyFilters.Attributes,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnChanged;
            _watcher.Error += OnError;

            Trace.WriteLine($"DirectoryWatcher started: {_path}");
        }

        public void Stop()
        {
            if (_watcher == null) return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnChanged;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;

            Trace.WriteLine($"DirectoryWatcher stopped: {_path}");
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            Changed?.Invoke(this, e);
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Trace.WriteLine($"DirectoryWatcher error: {e.GetException().Message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
