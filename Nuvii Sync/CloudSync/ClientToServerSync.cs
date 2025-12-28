using System;
using System.Collections.Concurrent;
using System.IO;
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
        private readonly ConcurrentDictionary<string, PendingOperation> _pendingOperations = new();
        private readonly TimeSpan _debounceDelay;
        private readonly int _maxRetries;
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
        /// </summary>
        public void OnCreated(string fullPath)
        {
            if (_disposed) return;

            var relativePath = GetRelativePath(fullPath);
            Trace.WriteLine($"[Client→Server] Created detected: {relativePath}");

            var operation = new PendingOperation
            {
                CurrentPath = fullPath,
                RelativePath = relativePath,
                Type = SyncOperationType.Create,
                State = OperationState.Pending,
                CreatedAt = DateTime.UtcNow
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
                        CreatedAt = existingOp.CreatedAt // Keep original time
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
            var renameOperation = new PendingOperation
            {
                CurrentPath = newFullPath,
                OriginalPath = oldFullPath,
                RelativePath = newRelativePath,
                OriginalRelativePath = oldRelativePath,
                Type = SyncOperationType.Rename,
                State = OperationState.Pending,
                CreatedAt = DateTime.UtcNow
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

            // Queue delete operation
            var deleteOperation = new PendingOperation
            {
                CurrentPath = fullPath,
                RelativePath = relativePath,
                Type = SyncOperationType.Delete,
                State = OperationState.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _pendingOperations.TryAdd(fullPath, deleteOperation);
            StartDebounceTimer(deleteOperation);
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

            // Queue modify operation
            var modifyOperation = new PendingOperation
            {
                CurrentPath = fullPath,
                RelativePath = relativePath,
                Type = SyncOperationType.Modified,
                State = OperationState.Pending,
                CreatedAt = DateTime.UtcNow
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
                        hr = CfApi.CfConvertToPlaceholder(
                            handle,
                            nint.Zero,  // No file identity needed
                            0,
                            CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC,
                            out _,
                            nint.Zero);

                        if (hr >= 0)
                        {
                            Trace.WriteLine($"  [InSync] Converted to placeholder: {Path.GetFileName(clientPath)}");
                        }
                        else
                        {
                            Trace.WriteLine($"  [InSync] CfConvertToPlaceholder failed: 0x{hr:X8}");
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
}
