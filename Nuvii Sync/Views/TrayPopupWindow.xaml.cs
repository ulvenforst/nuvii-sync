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
            // Set window size (OneDrive-like compact size)
            _appWindow.Resize(new Windows.Graphics.SizeInt32(380, 450));

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
            // Get cursor position (where user clicked the tray icon)
            GetCursorPos(out POINT cursorPos);

            // Get screen work area (excludes taskbar)
            var workArea = GetWorkArea();

            // Window dimensions
            const int windowWidth = 380;
            const int windowHeight = 450;
            const int marginX = 8;
            const int marginBottom = 12; // Extra margin above taskbar

            // Calculate X position - center on cursor, but keep within work area
            var x = cursorPos.X - (windowWidth / 2);
            if (x + windowWidth > workArea.Right - marginX)
                x = workArea.Right - windowWidth - marginX;
            if (x < workArea.Left + marginX)
                x = workArea.Left + marginX;

            // Calculate Y position - place above taskbar with margin
            var y = workArea.Bottom - windowHeight - marginBottom;

            _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
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

        public void AddActivity(string fileName, string folderPath, SyncActivityType activityType)
        {
            _viewModel.AddActivity(fileName, folderPath, activityType);
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

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
            Hide();
        }

        private void QuitButton_Click(object sender, RoutedEventArgs e)
        {
            App.Current.QuitApplication();
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
