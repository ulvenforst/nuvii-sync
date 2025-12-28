using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace Nuvii_Sync.Services
{
    /// <summary>
    /// Native system tray icon service using Shell_NotifyIcon.
    /// No external dependencies required.
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 1;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        private const int NIF_MESSAGE = 0x01;
        private const int NIF_ICON = 0x02;
        private const int NIF_TIP = 0x04;
        private const int NIF_SHOWTIP = 0x80;

        private const int NIM_ADD = 0x00;
        private const int NIM_MODIFY = 0x01;
        private const int NIM_DELETE = 0x02;
        private const int NIM_SETVERSION = 0x04;

        private const int NOTIFYICON_VERSION_4 = 4;

        private readonly nint _windowHandle;
        private nint _iconHandle;
        private readonly uint _id;
        private bool _isVisible;
        private bool _disposed;
        private string _tooltip = string.Empty;
        private readonly WndProcDelegate _wndProc;
        private nint _originalWndProc;

        public event EventHandler? LeftClick;
        public event EventHandler<TrayContextMenuEventArgs>? RightClick;

        public string Tooltip
        {
            get => _tooltip;
            set
            {
                _tooltip = value;
                if (_isVisible)
                {
                    UpdateIcon();
                }
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    if (value)
                        ShowIcon();
                    else
                        HideIcon();
                }
            }
        }

        public TrayIconService(nint windowHandle, string iconPath, string tooltip)
        {
            _id = 1;
            _tooltip = tooltip;
            _windowHandle = windowHandle;
            _wndProc = WndProc;

            LoadIconFromPath(iconPath);
            _originalWndProc = SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));
        }

        private void LoadIconFromPath(string iconPath)
        {
            // 1. Try .ico file from package location
            try
            {
                var packagePath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
                var fullPath = Path.Combine(packagePath, iconPath);

                if (File.Exists(fullPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    _iconHandle = LoadImage(nint.Zero, fullPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    if (_iconHandle != nint.Zero)
                    {
                        Debug.WriteLine($"Loaded tray icon from: {fullPath}");
                        return;
                    }
                }
            }
            catch { }

            // 2. Try to extract icon from the current executable
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    _iconHandle = ExtractIcon(nint.Zero, exePath, 0);
                    if (_iconHandle != nint.Zero)
                    {
                        Debug.WriteLine($"Loaded tray icon from executable: {exePath}");
                        return;
                    }
                }
            }
            catch { }

            // 3. Try to load from shell32.dll (folder sync icon)
            try
            {
                var shell32Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
                _iconHandle = ExtractIcon(nint.Zero, shell32Path, 276);
                if (_iconHandle != nint.Zero)
                {
                    Debug.WriteLine("Loaded tray icon from shell32.dll");
                    return;
                }
            }
            catch { }

            // 4. Fallback to default application icon
            _iconHandle = LoadIcon(nint.Zero, IDI_APPLICATION);
            Debug.WriteLine("Using default application icon for tray");
        }

        private void ShowIcon()
        {
            var data = CreateNotifyIconData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
            data.uCallbackMessage = WM_TRAYICON;
            data.hIcon = _iconHandle;
            data.szTip = _tooltip;

            Shell_NotifyIcon(NIM_ADD, ref data);

            data.uVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref data);
        }

        private void HideIcon()
        {
            var data = CreateNotifyIconData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
        }

        private void UpdateIcon()
        {
            var data = CreateNotifyIconData();
            data.uFlags = NIF_TIP | NIF_SHOWTIP;
            data.szTip = _tooltip;
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        private NOTIFYICONDATA CreateNotifyIconData()
        {
            return new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = _id,
            };
        }

        private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
        {
            if (msg == WM_TRAYICON)
            {
                var mouseMsg = (int)(lParam & 0xFFFF);

                switch (mouseMsg)
                {
                    case WM_LBUTTONUP:
                        LeftClick?.Invoke(this, EventArgs.Empty);
                        return nint.Zero;

                    case WM_RBUTTONUP:
                    case WM_CONTEXTMENU:
                        RightClick?.Invoke(this, new TrayContextMenuEventArgs());
                        return nint.Zero;
                }
            }

            return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_isVisible)
            {
                HideIcon();
            }

            if (_originalWndProc != nint.Zero)
            {
                SetWindowLongPtr(_windowHandle, GWLP_WNDPROC, _originalWndProc);
            }

            if (_iconHandle != nint.Zero)
            {
                DestroyIcon(_iconHandle);
            }

            GC.SuppressFinalize(this);
        }

        ~TrayIconService()
        {
            Dispose();
        }

        #region Native Methods

        private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

        private const int GWLP_WNDPROC = -4;
        private const int IMAGE_ICON = 1;
        private const int LR_LOADFROMFILE = 0x0010;
        private static readonly nint IDI_APPLICATION = new(32512);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern nint ExtractIcon(nint hInst, string lpszExeFileName, int nIconIndex);

        [DllImport("user32.dll")]
        private static extern nint LoadIcon(nint hInstance, nint lpIconName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint LoadImage(nint hInst, string name, int type, int cx, int cy, int fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(nint hIcon);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll")]
        private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint Msg, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public nint hWnd;
            public uint uID;
            public int uFlags;
            public int uCallbackMessage;
            public nint hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public nint hBalloonIcon;
        }

        #endregion
    }

    public class TrayContextMenuEventArgs : EventArgs
    {
        public MenuFlyout? Flyout { get; set; }
    }
}
