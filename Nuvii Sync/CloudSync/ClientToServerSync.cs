using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Nuvii_Sync.CloudSync.Native;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Handles client-to-server synchronization with debouncing and operation merging.
    ///
    /// Key features:
    /// - Queue per element (different files don't block each other)
    /// - Debounce timer (waits before syncing to allow rename after create)
    /// - Operation merging (Create + Rename = Create with final name)
    /// - Concurrent processing of different elements
    ///
    /// This solves the "New Folder" → "Mi Carpeta" rename duplication problem.
    /// </summary>
    public sealed class ClientToServerSync : IDisposable
    {
        private readonly string _clientFolder;
        private readonly string _serverFolder;
        // Use case-insensitive comparison for paths (Windows is case-insensitive)
        private readonly ConcurrentDictionary<string, PendingOperation> _pendingOperations =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _debounceDelay;
        private readonly int _maxRetries;

        // Track recently deleted files to detect Move operations (Delete + Create = Move)
        // When FileSystemWatcher fires Delete+Create instead of Renamed for cross-directory moves
        // Key is filename only (not path), so also case-insensitive
        private readonly ConcurrentDictionary<string, DeletedFileInfo> _recentlyDeleted =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _moveDetectionWindow = TimeSpan.FromSeconds(5);

        // Track paths that should be suppressed from server-to-client sync
        // This prevents feedback loops when our operations trigger server file events
        private readonly ConcurrentDictionary<string, DateTime> _suppressedPaths =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _suppressionDuration = TimeSpan.FromSeconds(5);

        private bool _disposed;

        public event EventHandler<SyncEventArgs>? OperationCompleted;
        public event EventHandler<SyncErrorEventArgs>? OperationFailed;

        /// <summary>
        /// Creates a new ClientToServerSync instance.
        /// </summary>
        /// <param name="clientFolder">The sync root folder on the client</param>
        /// <param name="serverFolder">The server folder to sync to</param>
        /// <param name="debounceDelayMs">Delay in ms before syncing (default: 3000ms)</param>
        /// <param name="maxRetries">Maximum retry attempts for failed operations (default: 3)</param>
        public ClientToServerSync(
            string clientFolder,
            string serverFolder,
            int debounceDelayMs = 3000,
            int maxRetries = 3)
        {
            _clientFolder = clientFolder;
            _serverFolder = serverFolder;
            _debounceDelay = TimeSpan.FromMilliseconds(debounceDelayMs);
            _maxRetries = maxRetries;
        }

        /// <summary>
        /// Called when a file or folder is created in the client folder.
        /// Detects Move operations when a Delete+Create happens within a short window.
        /// </summary>
        /// <param name="fullPath">Full path to the created file/folder</param>
        /// <param name="isPlaceholder">True if the file is a dehydrated placeholder</param>
        public void OnCreated(string fullPath, bool isPlaceholder = false)
        {
            if (_disposed) return;

            var relativePath = GetRelativePath(fullPath);
            var fileName = Path.GetFileName(fullPath);
            Trace.WriteLine($"[Client→Server] Created detected: {relativePath}{(isPlaceholder ? " (placeholder)" : "")}");

            // Check if this is actually a Move (Delete + Create with same filename)
            if (_recentlyDeleted.TryRemove(fileName, out var deletedInfo))
            {
                // Check if the delete was recent enough
                if (DateTime.UtcNow - deletedInfo.DeletedAt <= _moveDetectionWindow)
                {
                    // This is a Move! Cancel the pending Delete and create a Rename operation
                    Trace.WriteLine($"  Detected MOVE: {deletedInfo.RelativePath} → {relativePath}");

                    // Cancel the pending Delete operation for the SOURCE
                    if (_pendingOperations.TryRemove(deletedInfo.OriginalPath, out var pendingDelete))
                    {
                        CancelTimer(pendingDelete);
                    }

                    // Also cancel any pending Delete at the DESTINATION (handles Replace scenarios)
                    // When user replaces a file, Windows sends: Delete(dest) + Delete(src) + Create(dest)
                    // The Move will use overwrite:true, so the destination Delete is no longer needed
                    if (_pendingOperations.TryRemove(fullPath, out var pendingDestDelete))
                    {
                        CancelTimer(pendingDestDelete);
                        Trace.WriteLine($"  Cancelled pending Delete at destination (Replace detected)");
                    }

                    // Mark as not-in-sync to show sync indicator (blue arrows)
                    MarkAsNotInSync(fullPath);

                    // Create a Rename operation (which handles moves)
                    var moveOperation = new PendingOperation
                    {
                        CurrentPath = fullPath,
                        OriginalPath = deletedInfo.OriginalPath,
                        RelativePath = relativePath,
                        OriginalRelativePath = deletedInfo.RelativePath,
                        Type = SyncOperationType.Rename, // Rename handles both rename and move
                        State = OperationState.Pending,
                        CreatedAt = DateTime.UtcNow,
                        IsDirectory = deletedInfo.IsDirectory
                    };

                    _pendingOperations.AddOrUpdate(
                        fullPath,
                        _ =>
                        {
                            StartDebounceTimer(moveOperation);
                            return moveOperation;
                        },
                        (_, existing) =>
                        {
                            CancelTimer(existing);
                            StartDebounceTimer(moveOperation);
                            return moveOperation;
                        });
                    return;
                }
            }

            // If it's a placeholder and not a move, skip it
            // (It was created by server sync, not by the user)
            if (isPlaceholder)
            {
                Trace.WriteLine($"  Skipping placeholder (not a move): {relativePath}");
                return;
            }

            // Normal Create operation
            var operation = new PendingOperation
            {
                CurrentPath = fullPath,
                RelativePath = relativePath,
                Type = SyncOperationType.Create,
                State = OperationState.Pending,
                CreatedAt = DateTime.UtcNow,
                IsDirectory = Directory.Exists(fullPath)
            };

            // Try to add or update existing operation
            _pendingOperations.AddOrUpdate(
                fullPath,
                // Add new operation
                _ =>
                {
                    StartDebounceTimer(operation);
                    return operation;
                },
                // Update existing - shouldn't happen for Create, but handle it
                (_, existing) =>
                {
                    CancelTimer(existing);
                    StartDebounceTimer(operation);
                    return operation;
                });
        }

        /// <summary>
        /// Called when a file or folder is renamed in the client folder.
        /// </summary>
        public void OnRenamed(string oldFullPath, string newFullPath)
        {
            if (_disposed) return;

            var oldRelativePath = GetRelativePath(oldFullPath);
            var newRelativePath = GetRelativePath(newFullPath);
            Trace.WriteLine($"[Client→Server] Renamed detected: {oldRelativePath} → {newRelativePath}");

            // Check if there's a pending Create operation for the old path
            if (_pendingOperations.TryRemove(oldFullPath, out var existingOp))
            {
                CancelTimer(existingOp);

                if (existingOp.State == OperationState.Pending)
                {
                    // MERGE: Create + Rename = Create with new name
                    Trace.WriteLine($"  Merging: Create({oldRelativePath}) + Rename → Create({newRelativePath})");

                    var mergedOperation = new PendingOperation
                    {
                        CurrentPath = newFullPath,
                        RelativePath = newRelativePath,
                        Type = SyncOperationType.Create, // Still a Create, just with different path
                        State = OperationState.Pending,
                        CreatedAt = existingOp.CreatedAt, // Keep original time
                        IsDirectory = existingOp.IsDirectory
                    };

                    _pendingOperations.TryAdd(newFullPath, mergedOperation);
                    StartDebounceTimer(mergedOperation);
                    return;
                }
                else if (existingOp.State == OperationState.InProgress)
                {
                    // Create is in progress, need to queue a rename after it completes
                    Trace.WriteLine($"  Create in progress, queueing rename");
                    QueueRenameAfterCreate(existingOp, oldFullPath, newFullPath);
                    return;
                }
            }

            // No pending Create - this is a standalone rename of an existing file
            // Mark as not-in-sync to show sync indicator (blue arrows)
            MarkAsNotInSync(newFullPath);

            var renameOperation = new PendingOperation
            {
                CurrentPath = newFullPath,
                OriginalPath = oldFullPath,
                RelativePath = newRelativePath,
                OriginalRelativePath = oldRelativePath,
                Type = SyncOperationType.Rename,
                State = OperationState.Pending,
                CreatedAt = DateTime.UtcNow,
                IsDirectory = Directory.Exists(newFullPath)
            };

            _pendingOperations.AddOrUpdate(
                newFullPath,
                _ =>
                {
                    StartDebounceTimer(renameOperation);
                    return renameOperation;
                },
                (_, existing) =>
                {
                    CancelTimer(existing);
                    StartDebounceTimer(renameOperation);
                    return renameOperation;
                });
        }

        /// <summary>
        /// Called when a file or folder is deleted in the client folder.
        /// </summary>
        public void OnDeleted(string fullPath)
        {
            if (_disposed) return;

            var relativePath = GetRelativePath(fullPath);
            var fileName = Path.GetFileName(fullPath);
            Trace.WriteLine($"[Client→Server] Deleted detected: {relativePath}");

            // Check if there's a pending operation for this path
            if (_pendingOperations.TryRemove(fullPath, out var existingOp))
            {
                CancelTimer(existingOp);

                if (existingOp.State == OperationState.Pending && existingOp.Type == SyncOperationType.Create)
                {
                    // Create + Delete = Nothing (file was created and deleted before sync)
                    Trace.WriteLine($"  Merging: Create + Delete = No-op (never synced)");
                    return;
                }
            }

            // Determine if this was a directory by checking server (client path no longer exists)
            var serverPath = Path.Combine(_serverFolder, relativePath);
            var isDirectory = Directory.Exists(serverPath);

            // Track this delete for potential Move detection (Delete + Create = Move)
            // FileSystemWatcher sometimes fires Delete+Create instead of Renamed for cross-directory moves
            var deletedInfo = new DeletedFileInfo
            {
                OriginalPath = fullPath,
                RelativePath = relativePath,
                FileName = fileName,
                DeletedAt = DateTime.UtcNow,
                IsDirectory = isDirectory
            };
            _recentlyDeleted.AddOrUpdate(fileName, deletedInfo, (_, __) => deletedInfo);

            // Clean up old entries
            CleanupOldDeletedEntries();

            // Queue delete operation
            var deleteOperation = new PendingOperation
            {
                CurrentPath = fullPath,
                RelativePath = relativePath,
                Type = SyncOperationType.Delete,
                State = OperationState.Pending,
                CreatedAt = DateTime.UtcNow,
                IsDirectory = isDirectory
            };

            _pendingOperations.TryAdd(fullPath, deleteOperation);
            StartDebounceTimer(deleteOperation);
        }

        /// <summary>
        /// Removes old entries from the recently deleted tracking dictionary.
        /// </summary>
        private void CleanupOldDeletedEntries()
        {
            var cutoff = DateTime.UtcNow - _moveDetectionWindow - TimeSpan.FromSeconds(5);
            var toRemove = _recentlyDeleted
                .Where(kvp => kvp.Value.DeletedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _recentlyDeleted.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Called when a file is modified in the client folder.
        /// </summary>
        public void OnModified(string fullPath)
        {
            if (_disposed) return;

            // Skip directories - they don't have "modified" content
            if (Directory.Exists(fullPath)) return;

            var relativePath = GetRelativePath(fullPath);
            Trace.WriteLine($"[Client→Server] Modified detected: {relativePath}");

            // Check if there's a pending Create - if so, just reset the timer
            if (_pendingOperations.TryGetValue(fullPath, out var existingOp))
            {
                if (existingOp.State == OperationState.Pending)
                {
                    CancelTimer(existingOp);
                    StartDebounceTimer(existingOp);
                    return;
                }
            }

            // Queue modify operation (always a file, not directory)
            var modifyOperation = new PendingOperation
            {
                CurrentPath = fullPath,
                RelativePath = relativePath,
                Type = SyncOperationType.Modified,
                State = OperationState.Pending,
                CreatedAt = DateTime.UtcNow,
                IsDirectory = false
            };

            _pendingOperations.AddOrUpdate(
                fullPath,
                _ =>
                {
                    StartDebounceTimer(modifyOperation);
                    return modifyOperation;
                },
                (_, existing) =>
                {
                    CancelTimer(existing);
                    StartDebounceTimer(modifyOperation);
                    return modifyOperation;
                });
        }

        private void StartDebounceTimer(PendingOperation operation)
        {
            operation.TimerCts = new CancellationTokenSource();
            var token = operation.TimerCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceDelay, token);

                    if (!token.IsCancellationRequested)
                    {
                        await ExecuteOperationAsync(operation);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timer was cancelled, operation was merged or superseded
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"  Error in debounce timer: {ex.Message}");
                }
            }, token);
        }

        private static void CancelTimer(PendingOperation operation)
        {
            try
            {
                operation.TimerCts?.Cancel();
                operation.TimerCts?.Dispose();
                operation.TimerCts = null;
            }
            catch
            {
                // Ignore cancellation errors
            }
        }

        private async Task ExecuteOperationAsync(PendingOperation operation)
        {
            if (_disposed) return;

            operation.State = OperationState.InProgress;
            Trace.WriteLine($"[Client→Server] Executing: {operation.Type} {operation.RelativePath}");

            // Suppress server events for paths we're about to modify
            // This prevents feedback loops from our own file operations
            SuppressServerEvents(operation.RelativePath);
            if (operation.Type == SyncOperationType.Rename && !string.IsNullOrEmpty(operation.OriginalRelativePath))
            {
                // For rename/move, suppress both source and destination
                SuppressServerEvents(operation.OriginalRelativePath);
            }

            var retryCount = 0;
            Exception? lastException = null;

            while (retryCount < _maxRetries)
            {
                try
                {
                    switch (operation.Type)
                    {
                        case SyncOperationType.Create:
                            await ExecuteCreateAsync(operation);
                            break;
                        case SyncOperationType.Rename:
                            await ExecuteRenameAsync(operation);
                            break;
                        case SyncOperationType.Delete:
                            await ExecuteDeleteAsync(operation);
                            break;
                        case SyncOperationType.Modified:
                            await ExecuteModifiedAsync(operation);
                            break;
                    }

                    // Success
                    operation.State = OperationState.Completed;
                    _pendingOperations.TryRemove(operation.CurrentPath, out _);

                    Trace.WriteLine($"  Completed: {operation.Type} {operation.RelativePath}");
                    OperationCompleted?.Invoke(this, new SyncEventArgs(operation));
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    retryCount++;
                    Trace.WriteLine($"  Retry {retryCount}/{_maxRetries}: {ex.Message}");

                    if (retryCount < _maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount))); // Exponential backoff
                    }
                }
            }

            // Failed after all retries
            operation.State = OperationState.Failed;
            Trace.WriteLine($"  Failed after {_maxRetries} retries: {operation.Type} {operation.RelativePath}");
            OperationFailed?.Invoke(this, new SyncErrorEventArgs(operation, lastException));
        }

        private Task ExecuteCreateAsync(PendingOperation operation)
        {
            var serverPath = Path.Combine(_serverFolder, operation.RelativePath);
            var clientPath = operation.CurrentPath;

            // Check if source still exists
            var isDirectory = Directory.Exists(clientPath);
            var isFile = File.Exists(clientPath);

            if (!isDirectory && !isFile)
            {
                Trace.WriteLine($"  Source no longer exists, skipping: {operation.RelativePath}");
                return Task.CompletedTask;
            }

            if (isDirectory)
            {
                // Create directory on server
                Directory.CreateDirectory(serverPath);
                Trace.WriteLine($"  Created directory on server: {operation.RelativePath}");
            }
            else
            {
                // Create parent directory if needed
                var parentDir = Path.GetDirectoryName(serverPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                // Copy file to server
                File.Copy(clientPath, serverPath, overwrite: true);
                Trace.WriteLine($"  Copied file to server: {operation.RelativePath}");
            }

            // Mark as synced (auto-detects if placeholder or regular file)
            MarkAsInSync(clientPath);

            return Task.CompletedTask;
        }

        private Task ExecuteRenameAsync(PendingOperation operation)
        {
            if (string.IsNullOrEmpty(operation.OriginalRelativePath))
            {
                throw new InvalidOperationException("Rename operation missing original path");
            }

            var oldServerPath = Path.Combine(_serverFolder, operation.OriginalRelativePath);
            var newServerPath = Path.Combine(_serverFolder, operation.RelativePath);

            // Check if source exists on server
            var isDirectory = Directory.Exists(oldServerPath);
            var isFile = File.Exists(oldServerPath);

            if (!isDirectory && !isFile)
            {
                // Source doesn't exist on server - maybe it was never synced
                // Treat as a Create instead
                Trace.WriteLine($"  Source not on server, treating as Create: {operation.RelativePath}");
                return ExecuteCreateAsync(operation);
            }

            // Create parent directory if needed
            var parentDir = Path.GetDirectoryName(newServerPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            if (isDirectory)
            {
                Directory.Move(oldServerPath, newServerPath);
                Trace.WriteLine($"  Renamed directory on server: {operation.OriginalRelativePath} → {operation.RelativePath}");
            }
            else
            {
                File.Move(oldServerPath, newServerPath, overwrite: true);
                Trace.WriteLine($"  Renamed file on server: {operation.OriginalRelativePath} → {operation.RelativePath}");
            }

            // Update placeholder's FileIdentity to match new path
            // This is critical - without this, hydration will look for the old path
            UpdatePlaceholderIdentity(operation.CurrentPath, operation.RelativePath);

            // Mark as synced (auto-detects if placeholder or regular file)
            MarkAsInSync(operation.CurrentPath);

            return Task.CompletedTask;
        }

        private Task ExecuteDeleteAsync(PendingOperation operation)
        {
            var serverPath = Path.Combine(_serverFolder, operation.RelativePath);

            if (Directory.Exists(serverPath))
            {
                Directory.Delete(serverPath, recursive: true);
                Trace.WriteLine($"  Deleted directory on server: {operation.RelativePath}");
            }
            else if (File.Exists(serverPath))
            {
                File.Delete(serverPath);
                Trace.WriteLine($"  Deleted file on server: {operation.RelativePath}");
            }
            else
            {
                Trace.WriteLine($"  Already deleted on server: {operation.RelativePath}");
            }

            return Task.CompletedTask;
        }

        private Task ExecuteModifiedAsync(PendingOperation operation)
        {
            var serverPath = Path.Combine(_serverFolder, operation.RelativePath);
            var clientPath = operation.CurrentPath;

            if (!File.Exists(clientPath))
            {
                Trace.WriteLine($"  Source no longer exists, skipping: {operation.RelativePath}");
                return Task.CompletedTask;
            }

            // Create parent directory if needed
            var parentDir = Path.GetDirectoryName(serverPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // Copy updated file to server
            File.Copy(clientPath, serverPath, overwrite: true);
            Trace.WriteLine($"  Updated file on server: {operation.RelativePath}");

            // Mark as synced (auto-detects if placeholder or regular file)
            MarkAsInSync(clientPath);

            return Task.CompletedTask;
        }

        private void QueueRenameAfterCreate(PendingOperation createOp, string oldPath, string newPath)
        {
            // This handles the edge case where a Create is in progress when a Rename arrives
            // We need to wait for Create to complete, then execute the Rename
            createOp.PendingRename = (oldPath, newPath);
        }

        /// <summary>
        /// Marks a file or folder as synced (in-sync) after successful sync to server.
        /// Automatically detects if file is already a placeholder:
        /// - Regular files: Converts to placeholder with in-sync flag (CfConvertToPlaceholder)
        /// - Existing placeholders: Updates in-sync state (CfSetInSyncState)
        /// </summary>
        private void MarkAsInSync(string clientPath)
        {
            try
            {
                bool isDirectory = Directory.Exists(clientPath);
                if (!isDirectory && !File.Exists(clientPath))
                {
                    Trace.WriteLine($"  [InSync] File no longer exists: {clientPath}");
                    return;
                }

                // Check if file is already a placeholder using attributes
                bool isPlaceholder = IsPlaceholder(clientPath);

                // Open handle with backup semantics (needed for directories)
                uint flags = CfApi.FILE_FLAG_BACKUP_SEMANTICS;
                nint handle = CfApi.CreateFileW(
                    clientPath,
                    CfApi.GENERIC_READ | CfApi.GENERIC_WRITE,
                    CfApi.FILE_SHARE_READ | CfApi.FILE_SHARE_WRITE | CfApi.FILE_SHARE_DELETE,
                    nint.Zero,
                    CfApi.OPEN_EXISTING,
                    flags,
                    nint.Zero);

                if (handle == CfApi.INVALID_HANDLE_VALUE)
                {
                    Trace.WriteLine($"  [InSync] Failed to open handle: {clientPath}");
                    return;
                }

                try
                {
                    int hr;

                    if (isPlaceholder)
                    {
                        // Already a placeholder: Just update in-sync state
                        hr = CfApi.CfSetInSyncState(
                            handle,
                            CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC,
                            CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE,
                            out _);

                        if (hr >= 0)
                        {
                            Trace.WriteLine($"  [InSync] Marked as in-sync: {Path.GetFileName(clientPath)}");
                        }
                        else
                        {
                            Trace.WriteLine($"  [InSync] CfSetInSyncState failed: 0x{hr:X8}");
                        }
                    }
                    else
                    {
                        // Regular file: Convert to placeholder with in-sync flag
                        // IMPORTANT: Must provide FileIdentity for dehydration to work!
                        // The FileIdentity is the relative path used in FETCH_DATA callback
                        var relativePath = clientPath.StartsWith(_clientFolder, StringComparison.OrdinalIgnoreCase)
                            ? clientPath.Substring(_clientFolder.Length).TrimStart(Path.DirectorySeparatorChar)
                            : Path.GetFileName(clientPath);

                        var fileIdentityBytes = System.Text.Encoding.Unicode.GetBytes(relativePath + '\0');
                        var fileIdentityHandle = GCHandle.Alloc(fileIdentityBytes, GCHandleType.Pinned);

                        try
                        {
                            hr = CfApi.CfConvertToPlaceholder(
                                handle,
                                fileIdentityHandle.AddrOfPinnedObject(),
                                (uint)fileIdentityBytes.Length,
                                CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC,
                                out _,
                                nint.Zero);

                            if (hr >= 0)
                            {
                                Trace.WriteLine($"  [InSync] Converted to placeholder: {Path.GetFileName(clientPath)} (identity: {relativePath})");
                            }
                            else
                            {
                                Trace.WriteLine($"  [InSync] CfConvertToPlaceholder failed: 0x{hr:X8}");
                            }
                        }
                        finally
                        {
                            fileIdentityHandle.Free();
                        }
                    }
                }
                finally
                {
                    CfApi.CloseHandle(handle);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"  [InSync] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the FileIdentity of a placeholder after rename/move.
        /// This is critical - without this, hydration will fail because it looks for the old path.
        /// </summary>
        private void UpdatePlaceholderIdentity(string clientPath, string newRelativePath)
        {
            try
            {
                if (!File.Exists(clientPath) && !Directory.Exists(clientPath))
                    return;

                // Check if it's a placeholder first
                if (!IsPlaceholder(clientPath))
                    return;

                uint flags = CfApi.FILE_FLAG_BACKUP_SEMANTICS;
                nint handle = CfApi.CreateFileW(
                    clientPath,
                    CfApi.GENERIC_READ | CfApi.GENERIC_WRITE,
                    CfApi.FILE_SHARE_READ | CfApi.FILE_SHARE_WRITE | CfApi.FILE_SHARE_DELETE,
                    nint.Zero,
                    CfApi.OPEN_EXISTING,
                    flags,
                    nint.Zero);

                if (handle == CfApi.INVALID_HANDLE_VALUE)
                {
                    Trace.WriteLine($"  [UpdateIdentity] Failed to open handle: {clientPath}");
                    return;
                }

                try
                {
                    // Build new FileIdentity
                    var fileIdentityBytes = System.Text.Encoding.Unicode.GetBytes(newRelativePath + '\0');
                    var fileIdentityHandle = GCHandle.Alloc(fileIdentityBytes, GCHandleType.Pinned);

                    try
                    {
                        // Get current file metadata
                        var fileInfo = new FileInfo(clientPath);
                        var fsMetadata = new CF_FS_METADATA
                        {
                            FileSize = fileInfo.Length,
                            BasicInfo = new FILE_BASIC_INFO
                            {
                                FileAttributes = (uint)fileInfo.Attributes,
                                CreationTime = fileInfo.CreationTime.ToFileTime(),
                                LastAccessTime = fileInfo.LastAccessTime.ToFileTime(),
                                LastWriteTime = fileInfo.LastWriteTime.ToFileTime(),
                                ChangeTime = fileInfo.LastWriteTime.ToFileTime()
                            }
                        };

                        var hr = CfApi.CfUpdatePlaceholder(
                            handle,
                            in fsMetadata,
                            fileIdentityHandle.AddrOfPinnedObject(),
                            (uint)fileIdentityBytes.Length,
                            nint.Zero,
                            0,
                            CF_UPDATE_FLAGS.CF_UPDATE_FLAG_MARK_IN_SYNC,
                            out _,
                            nint.Zero);

                        if (hr >= 0)
                        {
                            Trace.WriteLine($"  [UpdateIdentity] Updated: {Path.GetFileName(clientPath)} → identity: {newRelativePath}");
                        }
                        else
                        {
                            Trace.WriteLine($"  [UpdateIdentity] CfUpdatePlaceholder failed: 0x{hr:X8}");
                        }
                    }
                    finally
                    {
                        fileIdentityHandle.Free();
                    }
                }
                finally
                {
                    CfApi.CloseHandle(handle);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"  [UpdateIdentity] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Marks a file or folder as NOT in-sync to show sync indicator.
        /// This causes the shell to display the sync arrows icon.
        /// Uses FILE_WRITE_ATTRIBUTES to avoid triggering hydration on dehydrated files.
        /// </summary>
        private void MarkAsNotInSync(string clientPath)
        {
            try
            {
                if (!File.Exists(clientPath) && !Directory.Exists(clientPath))
                    return;

                // Check if it's a placeholder first
                if (!IsPlaceholder(clientPath))
                    return;

                // Use FILE_WRITE_ATTRIBUTES instead of GENERIC_WRITE to avoid triggering hydration
                // Also use FILE_FLAG_OPEN_REPARSE_POINT to handle the placeholder directly
                uint flags = CfApi.FILE_FLAG_BACKUP_SEMANTICS | CfApi.FILE_FLAG_OPEN_REPARSE_POINT;
                nint handle = CfApi.CreateFileW(
                    clientPath,
                    CfApi.FILE_WRITE_ATTRIBUTES,
                    CfApi.FILE_SHARE_READ | CfApi.FILE_SHARE_WRITE | CfApi.FILE_SHARE_DELETE,
                    nint.Zero,
                    CfApi.OPEN_EXISTING,
                    flags,
                    nint.Zero);

                if (handle == CfApi.INVALID_HANDLE_VALUE)
                {
                    var lastError = CfApi.GetLastError();
                    Trace.WriteLine($"  [NotInSync] CreateFileW failed for {Path.GetFileName(clientPath)}: error {lastError}");
                    return;
                }

                try
                {
                    var hr = CfApi.CfSetInSyncState(
                        handle,
                        CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_NOT_IN_SYNC,
                        CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE,
                        out _);

                    if (hr >= 0)
                    {
                        Trace.WriteLine($"  [NotInSync] Marked for sync: {Path.GetFileName(clientPath)}");
                    }
                    else
                    {
                        Trace.WriteLine($"  [NotInSync] CfSetInSyncState failed for {Path.GetFileName(clientPath)}: 0x{hr:X8}");
                    }
                }
                finally
                {
                    CfApi.CloseHandle(handle);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"  [NotInSync] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a file or folder is already a Cloud Files placeholder.
        /// Uses CfGetPlaceholderStateFromAttributeTag for accurate detection.
        /// </summary>
        private static bool IsPlaceholder(string path)
        {
            try
            {
                // Get file attributes
                uint attributes = CfApi.GetFileAttributesW(path);
                if (attributes == CfApi.INVALID_FILE_ATTRIBUTES)
                    return false;

                // Check if it has reparse point attribute (placeholders are reparse points)
                const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
                if ((attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0)
                    return false;

                // Use CfGetPlaceholderStateFromAttributeTag for accurate detection
                var state = CfApi.CfGetPlaceholderStateFromAttributeTag(attributes, CfApi.IO_REPARSE_TAG_CLOUD);

                // Check if it's a placeholder (not invalid and has placeholder flag)
                return state != CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_INVALID &&
                       (state & CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PLACEHOLDER) != 0;
            }
            catch
            {
                return false;
            }
        }

        private string GetRelativePath(string fullPath)
        {
            if (fullPath.StartsWith(_clientFolder, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(_clientFolder.Length).TrimStart(Path.DirectorySeparatorChar);
            }
            return Path.GetFileName(fullPath);
        }

        #region Server Event Suppression

        /// <summary>
        /// Suppresses server-to-client sync events for a path.
        /// Used to prevent feedback loops when our operations trigger server file events.
        /// </summary>
        private void SuppressServerEvents(string relativePath)
        {
            var expiresAt = DateTime.UtcNow + _suppressionDuration;
            _suppressedPaths.AddOrUpdate(relativePath, expiresAt, (_, __) => expiresAt);
            Trace.WriteLine($"  [Suppress] Added: {relativePath}");
        }

        /// <summary>
        /// Checks if server events for a path should be suppressed.
        /// Called by CloudSyncProvider before reacting to server file events.
        /// </summary>
        public bool IsServerEventSuppressed(string relativePath)
        {
            // Clean up expired entries
            var now = DateTime.UtcNow;
            var expired = _suppressedPaths.Where(kvp => kvp.Value < now).Select(kvp => kvp.Key).ToList();
            foreach (var key in expired)
            {
                _suppressedPaths.TryRemove(key, out _);
            }

            // Check if path is suppressed
            if (_suppressedPaths.TryGetValue(relativePath, out var expiresAt))
            {
                if (now < expiresAt)
                {
                    Trace.WriteLine($"  [Suppress] Blocking server event for: {relativePath}");
                    return true;
                }
                _suppressedPaths.TryRemove(relativePath, out _);
            }

            return false;
        }

        #endregion

        /// <summary>
        /// Gets the count of pending operations.
        /// </summary>
        public int PendingCount => _pendingOperations.Count;

        /// <summary>
        /// Waits for all pending operations to complete.
        /// </summary>
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            while (_pendingOperations.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Cancel all pending timers
            foreach (var kvp in _pendingOperations)
            {
                CancelTimer(kvp.Value);
            }
            _pendingOperations.Clear();
        }
    }

    /// <summary>
    /// Types of sync operations.
    /// </summary>
    public enum SyncOperationType
    {
        Create,
        Rename,
        Delete,
        Modified
    }

    /// <summary>
    /// States of a sync operation.
    /// </summary>
    public enum OperationState
    {
        Pending,
        InProgress,
        Completed,
        Failed
    }

    /// <summary>
    /// Represents a pending sync operation.
    /// </summary>
    public class PendingOperation
    {
        public required string CurrentPath { get; set; }
        public string? OriginalPath { get; set; }
        public required string RelativePath { get; set; }
        public string? OriginalRelativePath { get; set; }
        public SyncOperationType Type { get; set; }
        public OperationState State { get; set; }
        public DateTime CreatedAt { get; set; }
        public CancellationTokenSource? TimerCts { get; set; }
        public (string OldPath, string NewPath)? PendingRename { get; set; }
        public bool IsDirectory { get; set; }
    }

    /// <summary>
    /// Event args for completed sync operations.
    /// </summary>
    public class SyncEventArgs : EventArgs
    {
        public PendingOperation Operation { get; }

        public SyncEventArgs(PendingOperation operation)
        {
            Operation = operation;
        }
    }

    /// <summary>
    /// Event args for failed sync operations.
    /// </summary>
    public class SyncErrorEventArgs : SyncEventArgs
    {
        public Exception? Exception { get; }

        public SyncErrorEventArgs(PendingOperation operation, Exception? exception)
            : base(operation)
        {
            Exception = exception;
        }
    }

    /// <summary>
    /// Tracks information about a recently deleted file for Move detection.
    /// Used to convert Delete+Create sequences into Move operations.
    /// </summary>
    internal class DeletedFileInfo
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime DeletedAt { get; set; }
        public bool IsDirectory { get; set; }
    }
}
