using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Nuvii_Sync.ViewModels;
using Nuvii_Sync.Models;

namespace Nuvii_Sync.Views
{
    /// <summary>
    /// Popup window shown when clicking the tray icon, similar to OneDrive.
    /// </summary>
    public sealed partial class TrayPopupWindow : Window
    {
        private readonly AppWindow _appWindow;
        private readonly nint _hWnd;
        private readonly SyncActivityViewModel _viewModel;

        public event EventHandler? SettingsRequested;

        public SyncActivityViewModel ViewModel => _viewModel;

        public TrayPopupWindow()
        {
            InitializeComponent();

            _viewModel = new SyncActivityViewModel();
            _viewModel.OpenSettingsRequested += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

            _hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            ConfigureWindow();
        }

        private void ConfigureWindow()
        {
            // Get DPI scale factor
            var dpi = GetDpiForWindow(_hWnd);
            var scaleFactor = dpi / 96.0;

            // Set window size (OneDrive-like compact size) - scaled for DPI
            var width = (int)(380 * scaleFactor);
            var height = (int)(450 * scaleFactor);
            _appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

            // Remove title bar for cleaner look
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(true, false);
            }

            // Set as tool window (doesn't show in taskbar)
            SetWindowLong(_hWnd, GWL_EXSTYLE, GetWindowLong(_hWnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);

            // Handle deactivation to auto-hide
            this.Activated += TrayPopupWindow_Activated;
        }

        private void TrayPopupWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // Hide when clicking outside
                Hide();
            }
        }

        public void ShowAtTrayPosition()
        {
            // Get DPI scale factor
            var dpi = GetDpiForWindow(_hWnd);
            var scaleFactor = dpi / 96.0;

            // Get cursor position (where user clicked the tray icon)
            GetCursorPos(out POINT cursorPos);

            // Get screen work area (excludes taskbar)
            var workArea = GetWorkArea();

            // Window dimensions (scaled for DPI)
            var windowWidth = (int)(380 * scaleFactor);
            var windowHeight = (int)(450 * scaleFactor);
            var marginX = (int)(8 * scaleFactor);
            var marginBottom = (int)(12 * scaleFactor);

            // Calculate X position - center on cursor, but keep within work area
            var x = cursorPos.X - (windowWidth / 2);
            if (x + windowWidth > workArea.Right - marginX)
                x = workArea.Right - windowWidth - marginX;
            if (x < workArea.Left + marginX)
                x = workArea.Left + marginX;

            // Calculate Y position - place above taskbar with margin
            var y = workArea.Bottom - windowHeight - marginBottom;

            _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
            _appWindow.Resize(new Windows.Graphics.SizeInt32(windowWidth, windowHeight));
            _appWindow.Show();
            SetForegroundWindow(_hWnd);
        }

        public void Hide()
        {
            _appWindow.Hide();
        }

        public void UpdateStatus(string status, bool isSyncing)
        {
            _viewModel.SetSyncing(isSyncing);
            if (!isSyncing && !string.IsNullOrEmpty(status))
            {
                StatusTextElement.Text = status;
            }
        }

        public void SetSyncFolder(string folderPath)
        {
            _viewModel.SyncRootFolder = folderPath;
        }

        public void AddActivity(string fileName, string folderPath, string fullPath, SyncActivityType activityType)
        {
            _viewModel.AddActivity(fileName, folderPath, fullPath, activityType);
            UpdateActivityVisibility();
        }

        private void UpdateActivityVisibility()
        {
            var hasActivity = _viewModel.RecentActivity.Count > 0;
            EmptyStatePanel.Visibility = hasActivity ? Visibility.Collapsed : Visibility.Visible;
            ActivityListView.Visibility = hasActivity ? Visibility.Visible : Visibility.Collapsed;

            if (hasActivity)
            {
                ActivityListView.ItemsSource = _viewModel.RecentActivity;
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenFolderCommand.Execute(null);
        }

        private void ViewOnlineButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ViewOnlineCommand.Execute(null);
        }

        private void ActivityListView_ItemClick(object sender, Microsoft.UI.Xaml.Controls.ItemClickEventArgs e)
        {
            if (e.ClickedItem is SyncActivityItem item && item.IsClickable)
            {
                _viewModel.OpenFile(item);
            }
        }

        private void FolderLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Controls.HyperlinkButton button &&
                button.DataContext is SyncActivityItem item)
            {
                var folderPath = item.FolderFullPath;

                // If folder was renamed/deleted, try to open sync root instead
                if (string.IsNullOrEmpty(folderPath) || !System.IO.Directory.Exists(folderPath))
                {
                    folderPath = _viewModel.SyncRootFolder;
                }

                if (!string.IsNullOrEmpty(folderPath) && System.IO.Directory.Exists(folderPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", folderPath);
                    }
                    catch { }
                }
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
            Hide();
        }

        private void QuitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.Current.QuitApplication();
        }

        private void PauseSync2Hours_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement pause sync for 2 hours
        }

        private void PauseSync8Hours_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement pause sync for 8 hours
        }

        private void PauseSyncIndefinitely_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement pause sync indefinitely
        }

        #region Native Methods

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SPI_GETWORKAREA = 0x0030;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X, Y;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(nint hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint hwnd);

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(int uiAction, int uiParam, ref RECT pvParam, int fWinIni);

        private static RECT GetWorkArea()
        {
            var rect = new RECT();
            SystemParametersInfo(SPI_GETWORKAREA, 0, ref rect, 0);
            return rect;
        }

        #endregion
    }
}
