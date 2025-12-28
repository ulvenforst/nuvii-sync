using System;
using System.Collections.Generic;
using System.Security.Principal;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Provider;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Registers and unregisters the sync root with the Windows Shell.
    /// Based on CloudMirror sample CloudProviderRegistrar.cpp
    /// 
    /// Uses Windows.Storage.Provider.StorageProviderSyncRootManager which is
    /// the WinRT API for registering sync roots with the Shell.
    /// </summary>
    public static class CloudProviderRegistrar
    {
        private const string STORAGE_PROVIDER_ID = "NuviiSync";
        private const string STORAGE_PROVIDER_ACCOUNT = "NuviiAccount";

        private static bool _isRegistered = false;

        public static bool IsRegistered => _isRegistered;

        /// <summary>
        /// Registers the sync root with the Windows Shell.
        /// Based on CloudProviderRegistrar::RegisterWithShell()
        /// </summary>
        public static void RegisterWithShell()
        {
            try
            {
                var syncRootId = GetSyncRootId();

                Trace.WriteLine($"Registering sync root: {syncRootId}");

                var folder = StorageFolder.GetFolderFromPathAsync(
                    ProviderFolderLocations.ClientFolder).AsTask().Result;

                var info = new StorageProviderSyncRootInfo
                {
                    Id = syncRootId,
                    Path = folder,
                    
                    // DisplayNameResource - shown in File Explorer navigation pane
                    DisplayNameResource = "Nuvii Sync",

                    // IconResource - using the Nuvii Sync icon from package assets
                    IconResource = GetIconResourcePath(),
                    
                    // HydrationPolicy.Full means files are fully hydrated on first access
                    // This matches the CloudMirror sample behavior
                    HydrationPolicy = StorageProviderHydrationPolicy.Full,
                    // AutoDehydrationAllowed is required to allow "Free up space" to dehydrate files
                    HydrationPolicyModifier = StorageProviderHydrationPolicyModifier.AutoDehydrationAllowed,
                    
                    // PopulationPolicy.AlwaysFull means all placeholders are created upfront
                    // (no on-demand population via CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS)
                    PopulationPolicy = StorageProviderPopulationPolicy.AlwaysFull,
                    
                    // InSyncPolicy determines what file attributes affect in-sync state
                    InSyncPolicy = StorageProviderInSyncPolicy.FileCreationTime |
                                   StorageProviderInSyncPolicy.DirectoryCreationTime,
                    
                    Version = "1.0.0",
                    ShowSiblingsAsGroup = false,
                    HardlinkPolicy = StorageProviderHardlinkPolicy.None
                };

                // Context - identity that maps server to client
                var syncRootIdentity = $"{ProviderFolderLocations.ServerFolder}->{ProviderFolderLocations.ClientFolder}";
                info.Context = CryptographicBuffer.ConvertStringToBinary(
                    syncRootIdentity, BinaryStringEncoding.Utf8);

                // Custom states - these show up as additional columns/icons in File Explorer
                var customStates = info.StorageProviderItemPropertyDefinitions;
                AddCustomState(customStates, "CustomStateName1", 1);
                AddCustomState(customStates, "CustomStateName2", 2);
                AddCustomState(customStates, "CustomStateName3", 3);

                StorageProviderSyncRootManager.Register(info);
                _isRegistered = true;

                Trace.WriteLine("Sync root registered successfully");

                // Give the cache some time to invalidate
                System.Threading.Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not register the sync root, hr 0x{ex.HResult:X8}");
                _isRegistered = false;
                throw;
            }
        }

        /// <summary>
        /// Unregisters the sync root from the Windows Shell.
        /// 
        /// NOTE: A real sync engine should NOT unregister upon exit.
        /// This is just for demonstration purposes.
        /// </summary>
        public static void Unregister()
        {
            try
            {
                var syncRootId = GetSyncRootId();
                Trace.WriteLine($"Unregistering: {syncRootId}");
                StorageProviderSyncRootManager.Unregister(syncRootId);
                _isRegistered = false;
                Trace.WriteLine("Unregistered successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not unregister the sync root, hr 0x{ex.HResult:X8}");
            }
        }

        /// <summary>
        /// Gets the sync root ID following the pattern: ProviderId!UserSid!AccountName
        /// </summary>
        public static string GetSyncRootId()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var userSid = identity.User?.Value ?? 
                throw new InvalidOperationException("Could not get user SID");
            
            return $"{STORAGE_PROVIDER_ID}!{userSid}!{STORAGE_PROVIDER_ACCOUNT}";
        }

        private static void AddCustomState(
            IList<StorageProviderItemPropertyDefinition> customStates,
            string displayName,
            int id)
        {
            customStates.Add(new StorageProviderItemPropertyDefinition
            {
                DisplayNameResource = displayName,
                Id = id
            });
        }

        /// <summary>
        /// Gets the icon resource path for the sync root.
        /// Returns the path to the Nuvii icon in the package assets.
        /// </summary>
        private static string GetIconResourcePath()
        {
            try
            {
                var installedPath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
                var iconPath = System.IO.Path.Combine(installedPath, "Assets", "NuviiLocalSync.ico");
                return $"{iconPath},0";
            }
            catch
            {
                // Fallback to system icon if package path is not available
                return "%SystemRoot%\\system32\\charmap.exe,0";
            }
        }
    }
}
