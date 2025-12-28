using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage.Provider;

namespace Nuvii_Sync.CloudSync.ShellServices
{
    /// <summary>
    /// Provides content URIs for cloud files.
    /// Based on CloudMirror sample UriSource.cpp
    ///
    /// This allows the system to get a web URI for a file and vice versa.
    /// </summary>
    [ComVisible(true)]
    [Guid(ShellServiceGuids.UriSource)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class UriSource : IStorageProviderUriSource
    {
        private const string UriPrefix = "http://nuviisync.example.com/contentUri/";
        private const string ContentIdPrefix = "http://nuviisync.example.com/contentId/";

        /// <summary>
        /// Given a content URI, returns the local file path.
        /// </summary>
        public void GetPathForContentUri(string contentUri, StorageProviderGetPathForContentUriResult result)
        {
            System.Diagnostics.Trace.WriteLine($"UriSource.GetPathForContentUri: {contentUri}");

            result.Status = StorageProviderUriSourceStatus.FileNotFound;

            try
            {
                if (!contentUri.StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase))
                    return;

                var clientFolder = ProviderFolderLocations.ClientFolder;
                if (string.IsNullOrEmpty(clientFolder))
                    return;

                // Extract the file name from the URI
                var uri = contentUri.Substring(UriPrefix.Length);
                var queryIndex = uri.IndexOf('?');
                if (queryIndex > 0)
                    uri = uri.Substring(0, queryIndex);

                // Build local path
                var localPath = Path.Combine(clientFolder, uri);

                if (File.Exists(localPath) || Directory.Exists(localPath))
                {
                    result.Path = localPath;
                    result.Status = StorageProviderUriSourceStatus.Success;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"UriSource.GetPathForContentUri error: {ex.Message}");
            }
        }

        /// <summary>
        /// Given a local file path, returns content URIs for the file.
        /// </summary>
        public void GetContentInfoForPath(string path, StorageProviderGetContentInfoForPathResult result)
        {
            System.Diagnostics.Trace.WriteLine($"UriSource.GetContentInfoForPath: {path}");

            result.Status = StorageProviderUriSourceStatus.FileNotFound;

            try
            {
                var fileName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(fileName))
                    return;

                // Generate content ID
                result.ContentId = ContentIdPrefix + fileName;

                // Generate content URI
                result.ContentUri = UriPrefix + fileName + "?StorageProviderId=NuviiSync";

                result.Status = StorageProviderUriSourceStatus.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"UriSource.GetContentInfoForPath error: {ex.Message}");
            }
        }
    }
}
