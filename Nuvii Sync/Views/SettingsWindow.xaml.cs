using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Nuvii_Sync.Views.Pages;

namespace Nuvii_Sync.Views
{
    /// <summary>
    /// Settings window for Nuvii Sync with navigation sidebar.
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        private readonly AppWindow _appWindow;
        private readonly nint _hWnd;

        public SettingsWindow()
        {
            InitializeComponent();

            _hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            _appWindow.Title = "Nuvii Sync - Configuración";
            _appWindow.Resize(new Windows.Graphics.SizeInt32(860, 600));

            // Hide the default system title bar
            ExtendsContentIntoTitleBar = true;
            // Replace system title bar with the WinUI TitleBar
            SetTitleBar(AppTitleBar);

            SetTaskbarIcon();
            OverlappedPresenter presenter = OverlappedPresenter.Create();
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(true, true);
            _appWindow.SetPresenter(presenter);

            _appWindow.Closing += AppWindow_Closing;

            // Set version in footer
            SetVersionInfo();

            // Hide Dev tab in Release mode
#if !DEBUG
            DevNavItem.Visibility = Visibility.Collapsed;
#endif

            // Navigate to first page
            NavView.SelectedItem = SyncBackupNavItem;
            ContentFrame.Navigate(typeof(SyncBackupPage));
        }

        private void SetVersionInfo()
        {
            try
            {
                var package = Windows.ApplicationModel.Package.Current;
                var version = package.Id.Version;
                var versionString = $"v{version.Major}.{version.Minor}.{version.Build}";
                VersionNavItem.Content = versionString;
            }
            catch
            {
                VersionNavItem.Content = "v1.0.0";
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                Type? pageType = tag switch
                {
                    "SyncBackup" => typeof(SyncBackupPage),
                    "Account" => typeof(AccountPage),
#if DEBUG
                    "Dev" => typeof(DevPage),
#endif
                    _ => null
                };

                if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
                {
                    ContentFrame.Navigate(pageType);
                }
            }
        }

        private void SetTaskbarIcon()
        {
            try
            {
                var iconPath = Path.Combine(
                    Windows.ApplicationModel.Package.Current.InstalledLocation.Path,
                    "Assets", "NuviiLocalSync.ico");
                if (File.Exists(iconPath))
                    _appWindow.SetIcon(iconPath);
            }
            catch { }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (App.Current.IsQuitting)
                return;

            args.Cancel = true;
            Hide();
        }

        public void Hide() => ShowWindow(_hWnd, SW_HIDE);
        public void Show()
        {
            ShowWindow(_hWnd, SW_SHOW);
            SetForegroundWindow(_hWnd);
        }

        #region Native Methods
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hWnd);
        #endregion
    }
}
