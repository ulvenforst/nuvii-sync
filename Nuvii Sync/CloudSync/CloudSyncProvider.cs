using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Nuvii_Sync.CloudSync.Native;
using Nuvii_Sync.CloudSync.ShellServices;
using Nuvii_Sync.Models;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Main orchestrator for the cloud sync provider.
    /// Based on CloudMirror sample FakeCloudProvider.cpp
    ///
    /// This implementation mirrors CloudMirror with additions:
    /// - FETCH_DATA and CANCEL_FETCH_DATA callbacks (CloudMirror)
    /// - DirectoryWatcher for pin/unpin (CloudMirror)
    /// - ServerWatcher for real-time sync from server (will be replaced by SignalR)
    /// - ClientToServerSync for client→server synchronization with debouncing
    /// </summary>
    public sealed class CloudSyncProvider : IDisposable
    {
        private CF_CONNECTION_KEY _connectionKey;
        private DirectoryWatcher? _clientWatcher;
        private ServerWatcher? _serverWatcher;
        private ClientToServerSync? _clientToServerSync;
        private ClientSyncWatcher? _clientSyncWatcher;
        private bool _isRunning;
        private bool _isConnected;
        private bool _disposed;

        // Callback delegates - must be kept alive
        private CF_CALLBACK? _fetchDataCallback;
        private CF_CALLBACK? _cancelFetchDataCallback;
        private CF_CALLBACK_REGISTRATION[]? _callbackTable;
        private GCHandle _fetchDataHandle;
        private GCHandle _cancelFetchDataHandle;
        private GCHandle _callbackTableHandle;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<SyncActivityEventArgs>? ActivityOccurred;

        public bool IsRunning => _isRunning;

        public CloudSyncProvider() { }

        /// <summary>
        /// Starts the cloud sync provider.
        /// Based on FakeCloudProvider::Start()
        /// </summary>
        public async Task<bool> StartAsync(string serverFolder, string clientFolder)
        {
            if (_isRunning)
            {
                Trace.WriteLine("Provider is already running");
                return false;
            }

            try
            {
                if (!ProviderFolderLocations.Initialize(serverFolder, clientFolder))
                {
                    Trace.WriteLine("Failed to initialize folder locations");
                    return false;
                }

                // Stage 1: Setup (from FakeCloudProvider::Start)
                StatusChanged?.Invoke(this, "Inicializando...");

                // Start Shell Services FIRST - registers COM objects for thumbnails, custom states, etc.
                StatusChanged?.Invoke(this, "Iniciando servicios de Shell...");
                ShellServices.ShellServices.InitAndStartServiceTask();

                await Task.Delay(500);

                // The client folder (syncroot) must be indexed for states to display
                StatusChanged?.Invoke(this, "Añadiendo carpeta al indexador de búsqueda...");
                await Task.Run(() => Utilities.AddFolderToSearchIndexer(ProviderFolderLocations.ClientFolder));

                // Register the provider with the shell
                StatusChanged?.Invoke(this, "Registrando con Shell...");
                await Task.Run(() => CloudProviderRegistrar.RegisterWithShell());

                // Hook up callback methods (FETCH_DATA, CANCEL_FETCH_DATA only - like CloudMirror)
                StatusChanged?.Invoke(this, "Conectando a raíz de sincronización...");
                await Task.Run(ConnectSyncRootTransferCallbacks);

                // Create the placeholders in the client folder
                StatusChanged?.Invoke(this, "Creando marcadores de posición...");
                await Task.Run(() => Placeholders.Create(
                    ProviderFolderLocations.ServerFolder,
                    "",
                    ProviderFolderLocations.ClientFolder));

                // Stage 2: Running
                // Start watcher for pin/unpin (attributes only) - like CloudMirror
                _clientWatcher = new DirectoryWatcher(ProviderFolderLocations.ClientFolder);
                _clientWatcher.Changed += OnSyncRootFileChanges;
                _clientWatcher.Start();

                // Start client→server sync with debouncing (handles Create+Rename merging)
                StatusChanged?.Invoke(this, "Iniciando sincronización cliente→servidor...");
                _clientToServerSync = new ClientToServerSync(
                    ProviderFolderLocations.ClientFolder,
                    ProviderFolderLocations.ServerFolder,
                    debounceDelayMs: 3000);
                _clientToServerSync.OperationCompleted += OnClientSyncCompleted;
                _clientToServerSync.OperationFailed += OnClientSyncFailed;

                _clientSyncWatcher = new ClientSyncWatcher(
                    ProviderFolderLocations.ClientFolder,
                    _clientToServerSync);
                _clientSyncWatcher.Start();

                // Start server watcher for real-time sync (DEMO - will be replaced by SignalR)
                _serverWatcher = new ServerWatcher(ProviderFolderLocations.ServerFolder);
                _serverWatcher.FileCreated += OnServerFileCreated;
                _serverWatcher.FileDeleted += OnServerFileDeleted;
                _serverWatcher.FileRenamed += OnServerFileRenamed;
                _serverWatcher.Start();

                _isRunning = true;
                StatusChanged?.Invoke(this, "En ejecución");

                Trace.WriteLine("=== Cloud sync provider started ===");
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to start: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error: {ex.Message}");
                await StopAsync(unregister: false);
                return false;
            }
        }

        /// <summary>
        /// Stops the cloud sync provider.
        /// Based on FakeCloudProvider shutdown sequence.
        /// </summary>
        public async Task StopAsync(bool unregister = false)
        {
            try
            {
                StatusChanged?.Invoke(this, "Deteniendo...");

                // Stop file watchers
                if (_clientWatcher != null)
                {
                    _clientWatcher.Changed -= OnSyncRootFileChanges;
                    _clientWatcher.Dispose();
                    _clientWatcher = null;
                }

                // Stop client→server sync
                if (_clientSyncWatcher != null)
                {
                    _clientSyncWatcher.Dispose();
                    _clientSyncWatcher = null;
                }

                if (_clientToServerSync != null)
                {
                    _clientToServerSync.OperationCompleted -= OnClientSyncCompleted;
                    _clientToServerSync.OperationFailed -= OnClientSyncFailed;
                    _clientToServerSync.Dispose();
                    _clientToServerSync = null;
                }

                if (_serverWatcher != null)
                {
                    _serverWatcher.FileCreated -= OnServerFileCreated;
                    _serverWatcher.FileDeleted -= OnServerFileDeleted;
                    _serverWatcher.FileRenamed -= OnServerFileRenamed;
                    _serverWatcher.Dispose();
                    _serverWatcher = null;
                }

                // Disconnect from sync root
                if (_isConnected)
                {
                    await Task.Run(DisconnectSyncRootTransferCallbacks);
                }

                // Stop Shell Services
                StatusChanged?.Invoke(this, "Deteniendo servicios de Shell...");
                ShellServices.ShellServices.Stop();

                // Unregister if requested (for demo/cleanup purposes)
                if (unregister)
                {
                    Trace.WriteLine("Unregistering sync root...");
                    await Task.Run(() => CloudProviderRegistrar.Unregister());
                    await Task.Run(() => SyncRootCleanup.ForceCleanup());
                }

                _isRunning = false;
                StatusChanged?.Invoke(this, "Detenido");

                Trace.WriteLine("Cloud sync provider stopped");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error stopping: {ex.Message}");
                StatusChanged?.Invoke(this, $"Error al detener: {ex.Message}");
            }
        }

        /// <summary>
        /// Forces cleanup - unregisters all Nuvii sync roots.
        /// </summary>
        public static async Task ForceCleanupAsync()
        {
            await Task.Run(() => SyncRootCleanup.ForceCleanup());
        }

        /// <summary>
        /// Connects to the sync root and registers callback handlers.
        /// Based on FakeCloudProvider::ConnectSyncRootTransferCallbacks()
        /// 
        /// CloudMirror only registers two callbacks:
        /// - FETCH_DATA: Called when cloud file needs to be hydrated
        /// - CANCEL_FETCH_DATA: Called when hydration is cancelled
        /// </summary>
        private void ConnectSyncRootTransferCallbacks()
        {
            Trace.WriteLine("Connecting to sync root...");

            // Create callback delegates
            _fetchDataCallback = OnFetchData;
            _cancelFetchDataCallback = OnCancelFetchData;

            // Pin delegates to prevent GC
            _fetchDataHandle = GCHandle.Alloc(_fetchDataCallback);
            _cancelFetchDataHandle = GCHandle.Alloc(_cancelFetchDataCallback);

            // Build callback table - identical to s_MirrorCallbackTable in CloudMirror
            _callbackTable = new[]
            {
                new CF_CALLBACK_REGISTRATION
                {
                    Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA,
                    Callback = Marshal.GetFunctionPointerForDelegate(_fetchDataCallback)
                },
                new CF_CALLBACK_REGISTRATION
                {
                    Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA,
                    Callback = Marshal.GetFunctionPointerForDelegate(_cancelFetchDataCallback)
                },
                CF_CALLBACK_REGISTRATION.CF_CALLBACK_REGISTRATION_END
            };

            _callbackTableHandle = GCHandle.Alloc(_callbackTable, GCHandleType.Pinned);

            var hr = CfApi.CfConnectSyncRoot(
                ProviderFolderLocations.ClientFolder,
                _callbackTable,
                nint.Zero,
                CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO |
                CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH,
                out _connectionKey);

            if (hr < 0)
            {
                Trace.WriteLine($"Could not connect to sync root, hr 0x{hr:X8}");
                FreeCallbackHandles();
                Marshal.ThrowExceptionForHR(hr);
            }

            _isConnected = true;
            Trace.WriteLine($"Connected to sync root, key: {_connectionKey.Internal}");
        }

        /// <summary>
        /// Disconnects from the sync root.
        /// Based on FakeCloudProvider::DisconnectSyncRootTransferCallbacks()
        /// </summary>
        private void DisconnectSyncRootTransferCallbacks()
        {
            if (!_isConnected) return;

            Trace.WriteLine("Disconnecting from sync root...");

            var hr = CfApi.CfDisconnectSyncRoot(_connectionKey);
            if (hr < 0)
            {
                Trace.WriteLine($"Could not disconnect the sync root, hr 0x{hr:X8}");
            }

            _connectionKey = default;
            _callbackTable = null;
            _isConnected = false;

            FreeCallbackHandles();
            Trace.WriteLine("Disconnected");
        }

        private void FreeCallbackHandles()
        {
            if (_fetchDataHandle.IsAllocated) _fetchDataHandle.Free();
            if (_cancelFetchDataHandle.IsAllocated) _cancelFetchDataHandle.Free();
            if (_callbackTableHandle.IsAllocated) _callbackTableHandle.Free();
        }

        #region Callbacks (CloudMirror: FakeCloudProvider callbacks)

        /// <summary>
        /// Called when the platform needs to fetch data from the cloud.
        /// Based on FakeCloudProvider::OnFetchData()
        /// </summary>
        private void OnFetchData(in CF_CALLBACK_INFO callbackInfo, nint callbackParameters)
        {
            try
            {
                Trace.WriteLine($"[FETCH_DATA] {callbackInfo.GetFullPath()}");

                if (callbackParameters == nint.Zero)
                {
                    Trace.WriteLine("  ERROR: callbackParameters is null");
                    return;
                }

                var fetchParams = Marshal.PtrToStructure<CF_CALLBACK_PARAMETERS_FETCHDATA>(callbackParameters);

                FileCopierWithProgress.CopyFromServerToClient(
                    callbackInfo,
                    fetchParams,
                    ProviderFolderLocations.ServerFolder);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"  ERROR in OnFetchData: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when a fetch operation is cancelled.
        /// Based on FakeCloudProvider::OnCancelFetchData()
        /// </summary>
        private void OnCancelFetchData(in CF_CALLBACK_INFO callbackInfo, nint callbackParameters)
        {
            try
            {
                Trace.WriteLine($"[CANCEL_FETCH_DATA] {callbackInfo.GetFullPath()}");

                if (callbackParameters != nint.Zero)
                {
                    var cancelParams = Marshal.PtrToStructure<CF_CALLBACK_PARAMETERS_CANCEL>(callbackParameters);
                    FileCopierWithProgress.CancelCopyFromServerToClient(callbackInfo, cancelParams);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"  ERROR in OnCancelFetchData: {ex.Message}");
            }
        }

        #endregion

        #region Directory Watcher (CloudMirror: CloudProviderSyncRootWatcher)

        /// <summary>
        /// Handles changes in the sync root folder.
        /// Based on CloudProviderSyncRootWatcher - only handles pin/unpin (attributes).
        /// </summary>
        private void OnSyncRootFileChanges(object? sender, FileSystemEventArgs e)
        {
            try
            {
                var path = e.FullPath;

                // Only handle attribute changes (pin/unpin)
                if (e.ChangeType != WatcherChangeTypes.Changed) return;
                if (!File.Exists(path)) return;

                var attributes = CfApi.GetFileAttributesW(path);
                if (attributes == CfApi.INVALID_FILE_ATTRIBUTES) return;

                // Skip directories
                if ((attributes & CfApi.FILE_ATTRIBUTE_DIRECTORY) != 0) return;

                // Handle pin (hydrate)
                if ((attributes & CfApi.FILE_ATTRIBUTE_PINNED) != 0)
                {
                    Trace.WriteLine($"[PIN] Hydrating: {path}");
                    HydrateFile(path);
                }
                // Handle unpin (dehydrate)
                else if ((attributes & CfApi.FILE_ATTRIBUTE_UNPINNED) != 0)
                {
                    Trace.WriteLine($"[UNPIN] Dehydrating: {path}");
                    DehydrateFile(path);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error processing file change: {ex.Message}");
            }
        }

        /// <summary>
        /// Hydrates a placeholder file (downloads content from server).
        /// Based on CloudMirror behavior when file is pinned.
        /// After hydration, marks the file as in-sync.
        /// </summary>
        private static void HydrateFile(string path)
        {
            var handle = CfApi.CreateFileW(
                path,
                CfApi.GENERIC_READ | CfApi.GENERIC_WRITE,
                CfApi.FILE_SHARE_READ | CfApi.FILE_SHARE_WRITE | CfApi.FILE_SHARE_DELETE,
                nint.Zero,
                CfApi.OPEN_EXISTING,
                CfApi.FILE_FLAG_BACKUP_SEMANTICS,
                nint.Zero);

            if (handle == CfApi.INVALID_HANDLE_VALUE) return;

            try
            {
                var hr = CfApi.CfHydratePlaceholder(handle, 0, -1, CF_HYDRATE_FLAGS.CF_HYDRATE_FLAG_NONE, nint.Zero);
                if (hr < 0)
                {
                    Trace.WriteLine($"  CfHydratePlaceholder failed: 0x{hr:X8}");
                    return;
                }

                // Mark as in-sync after successful hydration
                hr = CfApi.CfSetInSyncState(
                    handle,
                    CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC,
                    CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE,
                    out _);

                if (hr < 0)
                {
                    Trace.WriteLine($"  CfSetInSyncState after hydrate failed: 0x{hr:X8}");
                }
                else
                {
                    Trace.WriteLine($"  Hydrated and marked in-sync: {Path.GetFileName(path)}");
                }
            }
            finally
            {
                CfApi.CloseHandle(handle);
            }
        }

        /// <summary>
        /// Dehydrates a placeholder file (removes local content, keeps placeholder).
        /// Based on CloudMirror behavior when file is unpinned.
        /// After dehydration, marks the file as in-sync.
        /// </summary>
        private static void DehydrateFile(string path)
        {
            Trace.WriteLine($"  [Dehydrate] Starting: {path}");

            // First check if it's actually a placeholder and get its state
            var attributes = CfApi.GetFileAttributesW(path);
            if (attributes == CfApi.INVALID_FILE_ATTRIBUTES)
            {
                Trace.WriteLine($"  [Dehydrate] Failed to get attributes");
                return;
            }

            // Check placeholder state
            const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
            var isReparsePoint = (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
            var placeholderState = CfApi.CfGetPlaceholderStateFromAttributeTag(attributes, CfApi.IO_REPARSE_TAG_CLOUD);
            var isPlaceholder = placeholderState != CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_INVALID &&
                               (placeholderState & CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_PLACEHOLDER) != 0;
            var isInSync = (placeholderState & CF_PLACEHOLDER_STATE.CF_PLACEHOLDER_STATE_IN_SYNC) != 0;

            Trace.WriteLine($"  [Dehydrate] ReparsePoint={isReparsePoint}, Placeholder={isPlaceholder}, InSync={isInSync}, State={placeholderState}");

            // If it's not a placeholder, we need to convert it first
            if (!isPlaceholder)
            {
                Trace.WriteLine($"  [Dehydrate] File is NOT a placeholder - converting first");
                ConvertAndDehydrate(path);
                return;
            }

            // Check if already dehydrated (has OFFLINE attribute = no local data)
            const uint FILE_ATTRIBUTE_OFFLINE = 0x1000;
            if ((attributes & FILE_ATTRIBUTE_OFFLINE) != 0)
            {
                Trace.WriteLine($"  [Dehydrate] File already dehydrated, no action needed");
                return;  // Don't mark in-sync - preserve current sync state
            }

            // If not in-sync, we cannot dehydrate - mark in-sync first
            if (!isInSync)
            {
                Trace.WriteLine($"  [Dehydrate] File is NOT in-sync - marking in-sync first");
                MarkFileInSync(path);
                // Small delay to let the in-sync state propagate
                Task.Delay(100).Wait();
            }

            // Open handle for dehydration - use 0 access like CloudMirror
            var handle = CfApi.CreateFileW(
                path,
                0, // No access needed for CfDehydratePlaceholder
                CfApi.FILE_SHARE_READ | CfApi.FILE_SHARE_WRITE | CfApi.FILE_SHARE_DELETE,
                nint.Zero,
                CfApi.OPEN_EXISTING,
                CfApi.FILE_FLAG_BACKUP_SEMANTICS,
                nint.Zero);

            if (handle == CfApi.INVALID_HANDLE_VALUE)
            {
                var lastError = CfApi.GetLastError();
                Trace.WriteLine($"  [Dehydrate] CreateFileW failed, error: {lastError} (0x{lastError:X})");
                // Even if we can't dehydrate, try to mark in-sync to clear the pending state
                MarkFileInSync(path);
                return;
            }

            try
            {
                var hr = CfApi.CfDehydratePlaceholder(handle, 0, -1, CF_DEHYDRATE_FLAGS.CF_DEHYDRATE_FLAG_NONE, nint.Zero);
                if (hr < 0)
                {
                    // Common errors:
                    // 0x80070179 = ERROR_CLOUD_FILE_NOT_IN_SYNC
                    // 0x8007017A = ERROR_CLOUD_FILE_PINNED
                    // 0x8007017B = ERROR_CLOUD_FILE_NOT_A_CLOUD_FILE
                    Trace.WriteLine($"  [Dehydrate] CfDehydratePlaceholder failed: 0x{hr:X8}");

                    // Close handle before trying to fix
                    CfApi.CloseHandle(handle);
                    handle = CfApi.INVALID_HANDLE_VALUE;

                    // If dehydration failed, try to mark in-sync to clear pending state
                    Trace.WriteLine($"  [Dehydrate] Attempting to mark in-sync to clear pending state");
                    MarkFileInSync(path);
                    return;
                }

                Trace.WriteLine($"  [Dehydrate] CfDehydratePlaceholder succeeded");

                // Close handle and reopen with write access for SetInSyncState
                CfApi.CloseHandle(handle);
                handle = CfApi.INVALID_HANDLE_VALUE;

                // Mark as in-sync after successful dehydration
                MarkFileInSync(path);

                // Notify shell to refresh parent folder so its state reflects children's states
                var parentFolder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentFolder))
                {
                    NotifyShellOfChange(parentFolder);
                }

                Trace.WriteLine($"  [Dehydrate] Completed: {Path.GetFileName(path)}");
            }
            finally
            {
                if (handle != CfApi.INVALID_HANDLE_VALUE)
                    CfApi.CloseHandle(handle);
            }
        }

        /// <summary>
        /// Converts a regular file to a placeholder and dehydrates it.
        /// Used when "Free up space" is clicked on a file that's not yet a placeholder.
        /// </summary>
        private static void ConvertAndDehydrate(string path)
        {
            var handle = CfApi.CreateFileW(
                path,
                CfApi.GENERIC_READ | CfApi.GENERIC_WRITE,
                CfApi.FILE_SHARE_READ | CfApi.FILE_SHARE_WRITE | CfApi.FILE_SHARE_DELETE,
                nint.Zero,
                CfApi.OPEN_EXISTING,
                CfApi.FILE_FLAG_BACKUP_SEMANTICS,
                nint.Zero);

            if (handle == CfApi.INVALID_HANDLE_VALUE)
            {
                var lastError = CfApi.GetLastError();
                Trace.WriteLine($"  [ConvertAndDehydrate] CreateFileW failed: {lastError}");
                return;
            }

            try
            {
                // Build FileIdentity from relative path - required for dehydration to work
                var relativePath = path.StartsWith(ProviderFolderLocations.ClientFolder, StringComparison.OrdinalIgnoreCase)
                    ? path.Substring(ProviderFolderLocations.ClientFolder.Length).TrimStart(Path.DirectorySeparatorChar)
                    : Path.GetFileName(path);

                var fileIdentityBytes = System.Text.Encoding.Unicode.GetBytes(relativePath + '\0');
                var fileIdentityHandle = System.Runtime.InteropServices.GCHandle.Alloc(fileIdentityBytes, System.Runtime.InteropServices.GCHandleType.Pinned);

                try
                {
                    // Convert to placeholder with both MARK_IN_SYNC and DEHYDRATE flags
                    var hr = CfApi.CfConvertToPlaceholder(
                        handle,
                        fileIdentityHandle.AddrOfPinnedObject(),
                        (uint)fileIdentityBytes.Length,
                        CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC | CF_CONVERT_FLAGS.CF_CONVERT_FLAG_DEHYDRATE,
                        out _,
                        nint.Zero);

                    if (hr >= 0)
                    {
                        Trace.WriteLine($"  [ConvertAndDehydrate] Success: {Path.GetFileName(path)} (identity: {relativePath})");

                        // Notify shell to refresh parent folder so its state reflects children's states
                        var parentFolder = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(parentFolder))
                        {
                            NotifyShellOfChange(parentFolder);
                        }
                    }
                    else
                    {
                        Trace.WriteLine($"  [ConvertAndDehydrate] CfConvertToPlaceholder failed: 0x{hr:X8}");
                        // At least try to mark as in-sync
                        CfApi.CloseHandle(handle);
                        handle = CfApi.INVALID_HANDLE_VALUE;
                        MarkFileInSync(path);
                    }
                }
                finally
                {
                    fileIdentityHandle.Free();
                }
            }
            finally
            {
                if (handle != CfApi.INVALID_HANDLE_VALUE)
                    CfApi.CloseHandle(handle);
            }
        }

        /// <summary>
        /// Helper to mark a file as in-sync with a fresh handle.
        /// </summary>
        private static void MarkFileInSync(string path)
        {
            var handle = CfApi.CreateFileW(
                path,
                CfApi.GENERIC_WRITE,
                CfApi.FILE_SHARE_READ | CfApi.FILE_SHARE_WRITE | CfApi.FILE_SHARE_DELETE,
                nint.Zero,
                CfApi.OPEN_EXISTING,
                CfApi.FILE_FLAG_BACKUP_SEMANTICS,
                nint.Zero);

            if (handle == CfApi.INVALID_HANDLE_VALUE)
            {
                var lastError = CfApi.GetLastError();
                Trace.WriteLine($"  [MarkInSync] CreateFileW failed: {lastError}");
                return;
            }

            try
            {
                var hr = CfApi.CfSetInSyncState(
                    handle,
                    CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC,
                    CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE,
                    out _);

                if (hr >= 0)
                {
                    Trace.WriteLine($"  [MarkInSync] Success: {Path.GetFileName(path)}");
                }
                else
                {
                    Trace.WriteLine($"  [MarkInSync] CfSetInSyncState failed: 0x{hr:X8}");
                }
            }
            finally
            {
                CfApi.CloseHandle(handle);
            }
        }

        #endregion

        #region Client→Server Sync Handlers

        /// <summary>
        /// Called when a client→server sync operation completes successfully.
        /// </summary>
        private void OnClientSyncCompleted(object? sender, SyncEventArgs e)
        {
            Trace.WriteLine($"[Client→Server] Completed: {e.Operation.Type} {e.Operation.RelativePath}");

            // Notify shell to refresh the folder view
            NotifyShellOfChange(e.Operation.CurrentPath);

            // Skip Modified operations - only show significant actions (Create, Delete, Rename, Move)
            if (e.Operation.Type == SyncOperationType.Modified)
            {
                return;
            }

            // Handle directory Delete specially - notify for each file inside
            if (e.Operation.IsDirectory && e.Operation.Type == SyncOperationType.Delete)
            {
                // Notify for each file that was deleted inside the directory
                if (e.Operation.DeletedFilePaths != null)
                {
                    foreach (var relativePath in e.Operation.DeletedFilePaths)
                    {
                        var fullPath = Path.Combine(ProviderFolderLocations.ClientFolder, relativePath);
                        NotifyActivity(fullPath, SyncActivityType.Deleted);
                    }
                }
                return;
            }

            // Skip other directory operations - only show file activity in UI
            if (e.Operation.IsDirectory)
            {
                return;
            }

            // Notify activity for UI
            SyncActivityType activityType;
            if (e.Operation.Type == SyncOperationType.Rename)
            {
                // Determine if it's a Move (different directory) or Rename (same directory)
                var oldDir = Path.GetDirectoryName(e.Operation.OriginalRelativePath ?? "");
                var newDir = Path.GetDirectoryName(e.Operation.RelativePath ?? "");
                activityType = !string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)
                    ? SyncActivityType.Moved
                    : SyncActivityType.Renamed;
            }
            else
            {
                activityType = e.Operation.Type switch
                {
                    SyncOperationType.Create => SyncActivityType.Uploaded,
                    SyncOperationType.Delete => SyncActivityType.Deleted,
                    _ => SyncActivityType.Uploaded // Fallback, shouldn't happen
                };
            }
            NotifyActivity(e.Operation.CurrentPath, activityType);
        }

        /// <summary>
        /// Called when a client→server sync operation fails.
        /// </summary>
        private void OnClientSyncFailed(object? sender, SyncErrorEventArgs e)
        {
            Trace.WriteLine($"[Client→Server] Failed: {e.Operation.Type} {e.Operation.RelativePath} - {e.Exception?.Message}");
            StatusChanged?.Invoke(this, $"Error sincronizando: {e.Operation.RelativePath}");
        }

        #endregion

        #region Server Watcher Handlers (DEMO - will be replaced by SignalR)

        /// <summary>
        /// Called when a file/folder is created on the server.
        /// Creates a corresponding placeholder in the client folder.
        /// 
        /// In production: This will be triggered by a SignalR event from the backend.
        /// </summary>
        private void OnServerFileCreated(object? sender, FileSystemEventArgs e)
        {
            try
            {
                var serverPath = e.FullPath;
                var relativePath = GetRelativePath(serverPath, ProviderFolderLocations.ServerFolder);
                var clientPath = Path.Combine(ProviderFolderLocations.ClientFolder, relativePath);

                // Check if this event should be suppressed (caused by our own operation)
                if (_clientToServerSync?.IsServerEventSuppressed(relativePath) == true)
                {
                    return;
                }

                Trace.WriteLine($"[Server->Client] Creating placeholder: {relativePath}");

                // Use Placeholders.CreateSingle which handles both files and directories
                // Note: In production, activity notification will come from SignalR handler
                if (Placeholders.CreateSingle(serverPath, clientPath))
                {
                    NotifyShellOfChange(clientPath);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling server file created: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when a file/folder is deleted on the server.
        /// Deletes the corresponding placeholder in the client folder.
        /// 
        /// In production: This will be triggered by a SignalR event from the backend.
        /// </summary>
        private void OnServerFileDeleted(object? sender, FileSystemEventArgs e)
        {
            try
            {
                var serverPath = e.FullPath;
                var relativePath = GetRelativePath(serverPath, ProviderFolderLocations.ServerFolder);
                var clientPath = Path.Combine(ProviderFolderLocations.ClientFolder, relativePath);

                // Check if this event should be suppressed (caused by our own operation)
                if (_clientToServerSync?.IsServerEventSuppressed(relativePath) == true)
                {
                    return;
                }

                Trace.WriteLine($"[Server->Client] Deleting placeholder: {relativePath}");

                // Note: In production, activity notification will come from SignalR handler
                if (Placeholders.Delete(clientPath))
                {
                    NotifyShellOfChange(Path.GetDirectoryName(clientPath) ?? clientPath);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling server file deleted: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when a file/folder is renamed on the server.
        /// Renames the corresponding placeholder in the client folder.
        /// 
        /// In production: This will be triggered by a SignalR event from the backend.
        /// </summary>
        private void OnServerFileRenamed(object? sender, RenamedEventArgs e)
        {
            try
            {
                var oldRelativePath = GetRelativePath(e.OldFullPath, ProviderFolderLocations.ServerFolder);
                var newRelativePath = GetRelativePath(e.FullPath, ProviderFolderLocations.ServerFolder);

                // Check if this event should be suppressed (caused by our own operation)
                if (_clientToServerSync?.IsServerEventSuppressed(oldRelativePath) == true ||
                    _clientToServerSync?.IsServerEventSuppressed(newRelativePath) == true)
                {
                    return;
                }

                var oldClientPath = Path.Combine(ProviderFolderLocations.ClientFolder, oldRelativePath);
                var newClientPath = Path.Combine(ProviderFolderLocations.ClientFolder, newRelativePath);

                Trace.WriteLine($"[Server->Client] Renaming placeholder: {oldRelativePath} -> {newRelativePath}");

                if (Placeholders.Rename(oldClientPath, newClientPath))
                {
                    NotifyShellOfChange(newClientPath);
                    // Note: In production, activity notification will come from SignalR handler
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error handling server file renamed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the relative path from a base path.
        /// </summary>
        private static string GetRelativePath(string fullPath, string basePath)
        {
            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar);
            }
            return Path.GetFileName(fullPath);
        }

        /// <summary>
        /// Notifies the Windows Shell that a file has changed, forcing Explorer to refresh.
        /// </summary>
        private static void NotifyShellOfChange(string path)
        {
            try
            {
                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATH, path, null);
            }
            catch
            {
                // Shell notification is optional
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, string? dwItem1, string? dwItem2);

        private const uint SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNF_PATH = 0x0005;

        #endregion

        #region Activity Notification

        private void NotifyActivity(string fullPath, SyncActivityType activityType)
        {
            // Only notify for files, not directories
            if (Directory.Exists(fullPath)) return;

            var fileName = Path.GetFileName(fullPath);
            var folderName = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? "");

            ActivityOccurred?.Invoke(this, new SyncActivityEventArgs(fileName, folderName, fullPath, activityType));
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAsync(unregister: false).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Event args for sync activity notifications.
    /// </summary>
    public class SyncActivityEventArgs : EventArgs
    {
        public string FileName { get; }
        public string FolderName { get; }
        public string FullPath { get; }
        public SyncActivityType ActivityType { get; }

        public SyncActivityEventArgs(string fileName, string folderName, string fullPath, SyncActivityType activityType)
        {
            FileName = fileName;
            FolderName = folderName;
            FullPath = fullPath;
            ActivityType = activityType;
        }
    }
}
