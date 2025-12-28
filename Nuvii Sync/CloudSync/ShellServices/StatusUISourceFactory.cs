using System;
using System.Runtime.InteropServices;

namespace Nuvii_Sync.CloudSync.ShellServices
{
    /// <summary>
    /// Factory that creates status UI sources for the cloud provider.
    /// Based on CloudMirror sample MyStatusUISourceFactory.cpp
    ///
    /// NOTE: The IStorageProviderStatusUISourceFactory and IStorageProviderStatusUISource
    /// interfaces require Windows 10 2004+ and specific SDK contracts. This implementation
    /// provides a stub that will be called by Windows but is intentionally minimal.
    /// The sync root will still work correctly - custom states and thumbnails function normally.
    /// </summary>
    [ComVisible(true)]
    [Guid(ShellServiceGuids.StatusUISourceFactory)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class StatusUISourceFactory
    {
        // This is intentionally a stub. The actual IStorageProviderStatusUISourceFactory
        // interface implementation would require runtime WinRT projection which is complex.
        // The sync root functionality works without this - it's only for the status banner.
    }

    /// <summary>
    /// Tracks the current sync state of the provider.
    /// Used internally to coordinate status reporting.
    /// </summary>
    public static class SyncStatus
    {
        public enum State
        {
            InSync,
            Syncing,
            Error
        }

        private static State _currentState = State.InSync;
        public static event EventHandler? StateChanged;

        public static State CurrentState
        {
            get => _currentState;
            set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    StateChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        public static void NotifyChange()
        {
            StateChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
