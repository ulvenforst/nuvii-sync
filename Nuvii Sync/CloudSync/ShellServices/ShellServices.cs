using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Nuvii_Sync.CloudSync.ShellServices
{
    /// <summary>
    /// Registers COM objects that implement Shell services for cloud files.
    /// Based on CloudMirror sample ShellServices.cpp
    ///
    /// This class registers handlers for:
    /// - Custom states (icons in File Explorer)
    /// - Thumbnails
    /// - Context menus
    /// - URI source
    /// - Status UI
    /// </summary>
    public static class ShellServices
    {
        private static Thread? _serviceThread;
        private static bool _isRunning;
        private static readonly List<uint> _registeredCookies = new();
        private static ManualResetEvent? _stopEvent;

        /// <summary>
        /// Initializes and starts the shell services in a background thread.
        /// This must be called before registering the sync root.
        /// </summary>
        public static void InitAndStartServiceTask()
        {
            if (_isRunning)
            {
                System.Diagnostics.Trace.WriteLine("ShellServices already running");
                return;
            }

            System.Diagnostics.Trace.WriteLine("Starting ShellServices...");

            _stopEvent = new ManualResetEvent(false);
            _serviceThread = new Thread(ServiceThreadProc)
            {
                IsBackground = true,
                Name = "ShellServices"
            };
            _serviceThread.SetApartmentState(ApartmentState.STA);
            _serviceThread.Start();
        }

        /// <summary>
        /// Stops the shell services.
        /// </summary>
        public static void Stop()
        {
            if (!_isRunning)
                return;

            System.Diagnostics.Trace.WriteLine("Stopping ShellServices...");

            _stopEvent?.Set();
            _serviceThread?.Join(5000);
            _isRunning = false;
        }

        private static void ServiceThreadProc()
        {
            try
            {
                // Initialize COM for this thread as STA
                var hr = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.COINIT_APARTMENTTHREADED);
                if (hr < 0)
                {
                    System.Diagnostics.Trace.WriteLine($"CoInitializeEx failed: 0x{hr:X8}");
                    return;
                }

                try
                {
                    _isRunning = true;

                    // Register all COM class factories
                    RegisterClassObject<CustomStateProvider>(ShellServiceGuids.CustomStateProvider);
                    RegisterClassObject<ThumbnailProvider>(ShellServiceGuids.ThumbnailProvider);
                    RegisterClassObject<UriSource>(ShellServiceGuids.UriSource);
                    RegisterClassObject<ContextMenuHandler>(ShellServiceGuids.ContextMenuHandler);
                    RegisterClassObject<StatusUISourceFactory>(ShellServiceGuids.StatusUISourceFactory);

                    System.Diagnostics.Trace.WriteLine("ShellServices COM objects registered");

                    // Message pump - process COM calls until stop is signaled
                    while (!_stopEvent!.WaitOne(100))
                    {
                        // Process any pending COM messages
                        while (User32.PeekMessage(out var msg, IntPtr.Zero, 0, 0, User32.PM_REMOVE))
                        {
                            User32.TranslateMessage(ref msg);
                            User32.DispatchMessage(ref msg);
                        }
                    }

                    // Unregister all class objects
                    foreach (var cookie in _registeredCookies)
                    {
                        Ole32.CoRevokeClassObject(cookie);
                    }
                    _registeredCookies.Clear();

                    System.Diagnostics.Trace.WriteLine("ShellServices stopped");
                }
                finally
                {
                    Ole32.CoUninitialize();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ShellServices error: {ex.Message}");
            }
        }

        private static void RegisterClassObject<T>(string clsidString) where T : class, new()
        {
            try
            {
                var clsid = new Guid(clsidString);
                var factory = new ClassFactory<T>();
                var factoryPtr = Marshal.GetIUnknownForObject(factory);

                try
                {
                    var hr = Ole32.CoRegisterClassObject(
                        ref clsid,
                        factoryPtr,
                        Ole32.CLSCTX_LOCAL_SERVER,
                        Ole32.REGCLS_MULTIPLEUSE,
                        out var cookie);

                    if (hr >= 0)
                    {
                        _registeredCookies.Add(cookie);
                        System.Diagnostics.Trace.WriteLine($"Registered COM class: {typeof(T).Name} ({clsidString})");
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine($"Failed to register {typeof(T).Name}: 0x{hr:X8}");
                    }
                }
                finally
                {
                    Marshal.Release(factoryPtr);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error registering {typeof(T).Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Generic COM class factory for creating instances of registered COM classes.
    /// </summary>
    /// <typeparam name="T">The type to create instances of.</typeparam>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal class ClassFactory<T> : IClassFactory where T : class, new()
    {
        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;

            if (pUnkOuter != IntPtr.Zero)
                return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION

            try
            {
                var instance = new T();
                ppvObject = Marshal.GetIUnknownForObject(instance);

                if (riid != typeof(object).GUID && riid != IID_IUnknown)
                {
                    var hr = Marshal.QueryInterface(ppvObject, ref riid, out var specificPtr);
                    Marshal.Release(ppvObject);

                    if (hr < 0)
                    {
                        ppvObject = IntPtr.Zero;
                        return hr;
                    }

                    ppvObject = specificPtr;
                }

                return 0; // S_OK
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ClassFactory.CreateInstance error: {ex.Message}");
                return unchecked((int)0x80004005); // E_FAIL
            }
        }

        public int LockServer(bool fLock)
        {
            return 0; // S_OK
        }

        private static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    internal static class Ole32
    {
        public const uint COINIT_APARTMENTTHREADED = 0x2;
        public const uint CLSCTX_LOCAL_SERVER = 0x4;
        public const uint REGCLS_MULTIPLEUSE = 1;

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        public static extern int CoRegisterClassObject(
            ref Guid rclsid,
            IntPtr pUnk,
            uint dwClsContext,
            uint flags,
            out uint lpdwRegister);

        [DllImport("ole32.dll")]
        public static extern int CoRevokeClassObject(uint dwRegister);
    }

    internal static class User32
    {
        public const uint PM_REMOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);
    }
}
