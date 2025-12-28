using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Storage.Provider;

namespace Nuvii_Sync.CloudSync.ShellServices
{
    /// <summary>
    /// Provides custom state icons for cloud files in File Explorer.
    /// Based on CloudMirror sample CustomStateProvider.cpp
    ///
    /// This class implements IStorageProviderItemPropertySource which allows
    /// the provider to return custom properties (including icons) for files.
    /// </summary>
    [ComVisible(true)]
    [Guid(ShellServiceGuids.CustomStateProvider)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class CustomStateProvider : IStorageProviderItemPropertySource
    {
        /// <summary>
        /// Returns custom properties for a file, including custom state icons.
        /// Called by Windows Shell when displaying files in File Explorer.
        /// </summary>
        public IEnumerable<StorageProviderItemProperty> GetItemProperties(string itemPath)
        {
            System.Diagnostics.Trace.WriteLine($"CustomStateProvider.GetItemProperties: {itemPath}");

            var properties = new List<StorageProviderItemProperty>();

            try
            {
                // Use a hash of the path to assign different states to different files
                // This is just for demonstration - a real provider would check actual sync status
                var hash = itemPath.GetHashCode();

                if ((hash & 0x1) != 0)
                {
                    // Custom state 1 - Example: "Shared" status
                    var prop = new StorageProviderItemProperty
                    {
                        Id = 1,
                        Value = "Synced",
                        // Icon resource - using shell32.dll icons for demo
                        // In production, use your own branded icons
                        IconResource = "shell32.dll,-16769" // Green checkmark
                    };
                    properties.Add(prop);
                }

                if ((hash & 0x2) != 0)
                {
                    // Custom state 2 - Example: "Pinned" status
                    var prop = new StorageProviderItemProperty
                    {
                        Id = 2,
                        Value = "Available offline",
                        IconResource = "shell32.dll,-244" // Pin icon
                    };
                    properties.Add(prop);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"CustomStateProvider error: {ex.Message}");
            }

            return properties;
        }
    }
}
