using System;
using System.IO;
using Nuvii_Sync.CloudSync.Native;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Watches for file system changes in the client folder to sync to server.
    ///
    /// Detects: Created, Renamed, Deleted, and content changes.
    /// Ignores: Placeholder files (only syncs full/hydrated files).
    ///
    /// Works together with ClientToServerSync for debouncing and operation merging.
    /// </summary>
    public sealed class ClientSyncWatcher : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private readonly string _path;
        private readonly ClientToServerSync _syncHandler;
        private bool _disposed;

        public ClientSyncWatcher(string path, ClientToServerSync syncHandler)
        {
            _path = path;
            _syncHandler = syncHandler;
        }

        public void Start()
        {
            if (_watcher != null) return;

            _watcher = new FileSystemWatcher(_path)
            {
                // Watch for file/folder name changes and content changes
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnCreated;
            _watcher.Renamed += OnRenamed;
            _watcher.Deleted += OnDeleted;
            _watcher.Changed += OnChanged;
            _watcher.Error += OnError;

            Trace.WriteLine($"ClientSyncWatcher started: {_path}");
        }

        public void Stop()
        {
            if (_watcher == null) return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnCreated;
            _watcher.Renamed -= OnRenamed;
            _watcher.Deleted -= OnDeleted;
            _watcher.Changed -= OnChanged;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;

            Trace.WriteLine($"ClientSyncWatcher stopped: {_path}");
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Filter temporary files (Office ~$, .tmp, etc.) - silently ignore
                if (TempFileFilter.ShouldIgnore(e.FullPath))
                    return;

                // Check if this is a placeholder-only file (not hydrated)
                bool isPlaceholder = IsPlaceholderOnly(e.FullPath);

                // Always pass to sync handler - it needs to detect moves even for placeholders
                // The sync handler will skip placeholders that aren't part of a move operation
                _syncHandler.OnCreated(e.FullPath, isPlaceholder);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClientSyncWatcher] Error in OnCreated: {ex.Message}");
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                // Filter temporary files - if BOTH old and new are temp files, ignore completely
                // If only old is temp (renaming temp to final), let it through
                // If only new is temp (renaming final to temp), let it through (will be caught as delete of original)
                bool oldIsTemp = TempFileFilter.ShouldIgnoreByName(e.OldFullPath);
                bool newIsTemp = TempFileFilter.ShouldIgnore(e.FullPath);

                if (oldIsTemp && newIsTemp)
                    return;

                // If renaming FROM temp TO real file, treat as a Create of the new file
                if (oldIsTemp && !newIsTemp)
                {
                    bool isPlaceholder = IsPlaceholderOnly(e.FullPath);
                    _syncHandler.OnCreated(e.FullPath, isPlaceholder);
                    return;
                }

                // If renaming FROM real TO temp, treat as a Delete of the original
                if (!oldIsTemp && newIsTemp)
                {
                    _syncHandler.OnDeleted(e.OldFullPath);
                    return;
                }

                // Normal rename - neither is temp
                _syncHandler.OnRenamed(e.OldFullPath, e.FullPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClientSyncWatcher] Error in OnRenamed: {ex.Message}");
            }
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Filter temporary files by name (file no longer exists, can't check attributes)
                if (TempFileFilter.ShouldIgnoreByName(e.FullPath))
                    return;

                _syncHandler.OnDeleted(e.FullPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClientSyncWatcher] Error in OnDeleted: {ex.Message}");
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Skip directories - they don't have content changes
                if (Directory.Exists(e.FullPath)) return;

                // Filter temporary files - silently ignore
                if (TempFileFilter.ShouldIgnore(e.FullPath))
                    return;

                // Skip if this is a placeholder-only file
                if (IsPlaceholderOnly(e.FullPath))
                {
                    return;
                }

                _syncHandler.OnModified(e.FullPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClientSyncWatcher] Error in OnChanged: {ex.Message}");
            }
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            Trace.WriteLine($"[ClientSyncWatcher] Error: {e.GetException().Message}");

            // Try to restart the watcher
            try
            {
                Stop();
                Start();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ClientSyncWatcher] Failed to restart: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a file is a placeholder-only (not hydrated).
        /// We only want to sync files that have actual content.
        /// </summary>
        private static bool IsPlaceholderOnly(string path)
        {
            try
            {
                // For directories, they're never "placeholder-only" in the same sense
                if (Directory.Exists(path)) return false;

                if (!File.Exists(path)) return false;

                var attributes = CfApi.GetFileAttributesW(path);
                if (attributes == CfApi.INVALID_FILE_ATTRIBUTES) return false;

                // Check for OFFLINE attribute - indicates dehydrated placeholder
                const uint FILE_ATTRIBUTE_OFFLINE = 0x1000;

                // If it has OFFLINE attribute, it's a dehydrated placeholder
                if ((attributes & FILE_ATTRIBUTE_OFFLINE) != 0)
                {
                    return true;
                }

                // Also check using CfGetPlaceholderStateFromAttributeTag
                // Placeholder files that are not fully hydrated should not be synced
                var state = CfApi.CfGetPlaceholderStateFromAttributeTag(attributes, CfApi.IO_REPARSE_TAG_CLOUD);

                // If it's a placeholder but not in-sync and not partially hydrated, skip it
                // We only want to sync fully hydrated files or new files created by the user
                if ((state & CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PLACEHOLDER) != 0 &&
                    (state & CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC) != 0 &&
                    (state & CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PARTIAL) != 0)
                {
                    // This is a partial placeholder - skip
                    return true;
                }

                return false;
            }
            catch
            {
                // If we can't determine, assume it's not a placeholder
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
