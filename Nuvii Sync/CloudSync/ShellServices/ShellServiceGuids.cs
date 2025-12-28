namespace Nuvii_Sync.CloudSync.ShellServices
{
    /// <summary>
    /// GUIDs for COM class registration.
    /// These must match the CLSIDs in Package.appxmanifest.
    /// </summary>
    public static class ShellServiceGuids
    {
        // Custom State Handler - Shows custom icons/states in File Explorer
        public const string CustomStateProvider = "7C3B6E67-A8E1-4B1E-9D4F-5A2B3C8E9F01";

        // Thumbnail Provider - Provides thumbnails for placeholder files
        public const string ThumbnailProvider = "7C3B6E67-A8E1-4B1E-9D4F-5A2B3C8E9F02";

        // Extended Property Handler
        public const string ExtendedPropertyHandler = "7C3B6E67-A8E1-4B1E-9D4F-5A2B3C8E9F03";

        // Status UI Source Factory - Provides status UI in File Explorer
        public const string StatusUISourceFactory = "7C3B6E67-A8E1-4B1E-9D4F-5A2B3C8E9F04";

        // Context Menu Handler
        public const string ContextMenuHandler = "7C3B6E67-A8E1-4B1E-9D4F-5A2B3C8E9F05";

        // URI Source
        public const string UriSource = "7C3B6E67-A8E1-4B1E-9D4F-5A2B3C8E9F06";
    }
}
