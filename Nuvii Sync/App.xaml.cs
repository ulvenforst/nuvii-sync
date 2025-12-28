using System;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Nuvii_Sync.Views;
using Nuvii_Sync.Models;
using Nuvii_Sync.Services;

namespace Nuvii_Sync
{
    /// <summary>
    /// Application entry point with tray icon support.
    /// Uses native Shell_NotifyIcon for WinUI 3 tray icon implementation.
    /// </summary>
    public partial class App : Application
    {
        private TrayIconService? _trayIcon;
        private TrayPopupWindow? _popupWindow;
        private SettingsWindow? _settingsWindow;
        private bool _isQuitting;

        public static new App Current => (App)Application.Current;
        public TrayPopupWindow? PopupWindow => _popupWindow;
        public bool IsQuitting => _isQuitting;

        public Window? GetSettingsWindow() => _settingsWindow;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Create settings window (hidden by default, we need its handle for the tray icon)
            _settingsWindow = new SettingsWindow();

            // Get window handle for tray icon messages
            var hWnd = WindowNative.GetWindowHandle(_settingsWindow);

            // Create the popup window (always exists, shown/hidden on tray click)
            _popupWindow = new TrayPopupWindow();
            _popupWindow.SettingsRequested += PopupWindow_SettingsRequested;

            // Create and configure native tray icon with the custom icon
            _trayIcon = new TrayIconService(hWnd, "Assets\\NuviiLocalSync.ico", "Nuvii Sync");

            // Left click shows the popup
            _trayIcon.LeftClick += TrayIcon_LeftClick;

            // Right click also shows the popup (has all actions including Quit)
            _trayIcon.RightClick += TrayIcon_RightClick;

            _trayIcon.IsVisible = true;

            // Don't show main window on startup - only through tray popup settings button
        }

        private void TrayIcon_LeftClick(object? sender, EventArgs e)
        {
            _popupWindow?.ShowAtTrayPosition();
        }

        private void TrayIcon_RightClick(object? sender, TrayContextMenuEventArgs e)
        {
            _popupWindow?.ShowAtTrayPosition();
        }

        private void PopupWindow_SettingsRequested(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        public void ShowMainWindow()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow();
            }
            _settingsWindow.Activate();
        }

        public void HideMainWindow()
        {
            _settingsWindow?.Hide();
        }

        public void QuitApplication()
        {
            _isQuitting = true;

            // Dispose tray icon first to remove it from system tray
            _trayIcon?.Dispose();
            _trayIcon = null;

            // Close windows
            try { _popupWindow?.Close(); } catch { }
            try { _settingsWindow?.Close(); } catch { }

            // Force terminate the process - Exit() alone doesn't always work in WinUI 3
            Environment.Exit(0);
        }

        public void UpdateTrayTooltip(string tooltip)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Tooltip = tooltip;
            }
        }

        public void SetSyncFolder(string folderPath)
        {
            _popupWindow?.SetSyncFolder(folderPath);
        }

        public void UpdateSyncStatus(string status, bool isSyncing)
        {
            _popupWindow?.UpdateStatus(status, isSyncing);
        }

        public void AddSyncActivity(string fileName, string folderPath, SyncActivityType activityType)
        {
            _popupWindow?.AddActivity(fileName, folderPath, activityType);
        }
    }
}
