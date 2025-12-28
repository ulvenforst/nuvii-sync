using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Nuvii_Sync.CloudSync;
using Nuvii_Sync.Services;

namespace Nuvii_Sync.Views.Pages
{
    /// <summary>
    /// Development page for Nuvii Sync cloud files provider configuration.
    /// Uses SyncService singleton to manage sync provider lifecycle.
    /// </summary>
    public sealed partial class DevPage : Page
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly StringBuilder _logBuilder = new();
        private readonly DevPageTraceListener _traceListener;

        // Use singleton service instead of creating own instance
        private SyncService SyncService => SyncService.Instance;

        public DevPage()
        {
            InitializeComponent();

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // Subscribe to sync service events
            SyncService.StatusChanged += SyncService_StatusChanged;
            SyncService.ActivityOccurred += SyncService_ActivityOccurred;

            LoadSavedPaths();
            CheckExistingRegistration();
            UpdateButtonStates();

            // Register trace listener for debug output
            _traceListener = new DevPageTraceListener(this);
            Trace.Listeners.Add(_traceListener);

            // Handle cleanup when page is unloaded
            this.Unloaded += DevPage_Unloaded;
        }

        private void DevPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Remove trace listener to prevent memory leaks
            Trace.Listeners.Remove(_traceListener);

            // Unsubscribe from service events (but don't dispose the singleton)
            SyncService.StatusChanged -= SyncService_StatusChanged;
            SyncService.ActivityOccurred -= SyncService_ActivityOccurred;
        }

        private void CheckExistingRegistration()
        {
            if (SyncService.HasOrphanedRegistration())
            {
                Log("Se encontró un registro existente de Nuvii Sync.");
                Log("La raíz de sincronización sigue registrada de una sesión anterior.");
                Log("Haz clic en 'Iniciar Sincronización' para reconectar, o 'Desregistrar' para eliminarlo.");
            }
        }

        private void LoadSavedPaths()
        {
            ServerFolderTextBox.Text = SettingsService.ServerFolder ?? "";
            ClientFolderTextBox.Text = SettingsService.ClientFolder ?? "";
        }

        private void UpdateButtonStates()
        {
            var isRunning = SyncService.IsRunning;
            StartButton.IsEnabled = !isRunning;
            StopButton.IsEnabled = isRunning;
            OpenFolderButton.IsEnabled = isRunning || !string.IsNullOrEmpty(ClientFolderTextBox.Text);
            CleanupButton.IsEnabled = !isRunning;

            if (isRunning)
            {
                StatusTextBlock.Text = "En ejecución";
            }
        }

        private void SavePaths()
        {
            SettingsService.SavePaths(ServerFolderTextBox.Text, ClientFolderTextBox.Text);
        }

        private async void BrowseServerFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolderAsync();
            if (folder != null)
            {
                ServerFolderTextBox.Text = folder.Path;
                SavePaths();
                Log($"Carpeta del servidor: {folder.Path}");
            }
        }

        private async void BrowseClientFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolderAsync();
            if (folder != null)
            {
                ClientFolderTextBox.Text = folder.Path;
                SavePaths();
                Log($"Carpeta de sincronización: {folder.Path}");
                UpdateButtonStates();
            }
        }

        private async Task<StorageFolder?> PickFolderAsync()
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(
                App.Current.GetSettingsWindow());
            InitializeWithWindow.Initialize(picker, hWnd);

            return await picker.PickSingleFolderAsync();
        }

        private async void StartSync_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ServerFolderTextBox.Text) ||
                string.IsNullOrEmpty(ClientFolderTextBox.Text))
            {
                Log("Por favor, selecciona ambas carpetas primero.");
                return;
            }

            StartButton.IsEnabled = false;
            CleanupButton.IsEnabled = false;

            var success = await SyncService.StartAsync(
                ServerFolderTextBox.Text,
                ClientFolderTextBox.Text);

            if (success)
            {
                UpdateButtonStates();

                // Update App tray icon and popup
                App.Current.UpdateTrayTooltip("Nuvii Sync - En ejecución");
                App.Current.SetSyncFolder(ClientFolderTextBox.Text);
                App.Current.UpdateSyncStatus("Tus archivos están sincronizados", false);

                Log("¡Proveedor de sincronización iniciado!");
                Log("La raíz de sincronización permanecerá en el Explorador incluso después de cerrar la app.");
            }
            else
            {
                StartButton.IsEnabled = true;
                CleanupButton.IsEnabled = true;
                Log("Error al iniciar. Revisa el registro para más detalles.");
            }
        }

        private async void StopSync_Click(object sender, RoutedEventArgs e)
        {
            StopButton.IsEnabled = false;

            await SyncService.StopAsync();

            UpdateButtonStates();

            App.Current.UpdateTrayTooltip("Nuvii Sync - Detenido");
            App.Current.UpdateSyncStatus("Sincronización detenida", false);

            Log("Detenido. La raíz de sincronización permanece registrada.");
        }

        private async void Cleanup_Click(object sender, RoutedEventArgs e)
        {
            CleanupButton.IsEnabled = false;
            Log("Desregistrando todas las raíces de sincronización de Nuvii...");

            try
            {
                await SyncService.ForceCleanupAsync();
                Log("Raíces de sincronización desregistradas.");
                Log("El Explorador de archivos se reiniciará para aplicar los cambios.");
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
            }
            finally
            {
                UpdateButtonStates();
            }
        }

        private void OpenSyncFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = ProviderFolderLocations.IsInitialized
                ? ProviderFolderLocations.ClientFolder
                : ClientFolderTextBox.Text;

            if (!string.IsNullOrEmpty(folderPath))
            {
                try { Process.Start("explorer.exe", folderPath); }
                catch { }
            }
        }

        private void SyncService_StatusChanged(object? sender, string status)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusTextBlock.Text = status;

                // Update tray popup status
                var isSyncing = status.Contains("Sincronizando") || status.Contains("Inicializando");
                App.Current.UpdateSyncStatus(status, isSyncing);
            });
        }

        private void SyncService_ActivityOccurred(object? sender, SyncActivityEventArgs e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                App.Current.AddSyncActivity(e.FileName, e.FolderName, e.FullPath, e.ActivityType);
            });
        }

        private void Log(string message)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                _logBuilder.AppendLine($"[{timestamp}] {message}");
                LogTextBlock.Text = _logBuilder.ToString();
                LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
            });
        }

        internal void LogDebug(string message)
        {
            Log(message);
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(_logBuilder.ToString());
                Clipboard.SetContent(dataPackage);
                Log("¡Registro copiado al portapapeles!");
            }
            catch { }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logBuilder.Clear();
            LogTextBlock.Text = "";
        }
    }

    /// <summary>
    /// Trace listener that redirects Debug/Trace output to the DevPage log.
    /// </summary>
    internal class DevPageTraceListener : TraceListener
    {
        private readonly DevPage _page;

        public DevPageTraceListener(DevPage page)
        {
            _page = page;
        }

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                _page.LogDebug(message);
        }
    }
}
