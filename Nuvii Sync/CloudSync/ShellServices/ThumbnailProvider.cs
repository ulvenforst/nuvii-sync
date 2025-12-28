using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Nuvii_Sync.CloudSync.ShellServices
{
    /// <summary>
    /// Provides thumbnails for placeholder files.
    /// Based on CloudMirror sample ThumbnailProvider.cpp
    ///
    /// Implements IThumbnailProvider and IInitializeWithItem to provide
    /// thumbnails for cloud files by delegating to the source file.
    /// </summary>
    [ComVisible(true)]
    [Guid(ShellServiceGuids.ThumbnailProvider)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ThumbnailProvider : IThumbnailProvider, IInitializeWithItem
    {
        private IShellItemBase? _sourceItem;
        private string _sourcePath = string.Empty;

        /// <summary>
        /// Initialize with the shell item representing the placeholder file.
        /// We find the corresponding source file to get its thumbnail.
        /// </summary>
        public int Initialize(IShellItemBase psi, uint grfMode)
        {
            try
            {
                // Get the path of the placeholder file
                psi.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var destPath);

                if (string.IsNullOrEmpty(destPath))
                    return HResults.E_FAIL;

                System.Diagnostics.Trace.WriteLine($"ThumbnailProvider.Initialize: {destPath}");

                var clientFolder = ProviderFolderLocations.ClientFolder;
                var serverFolder = ProviderFolderLocations.ServerFolder;

                if (string.IsNullOrEmpty(clientFolder) || string.IsNullOrEmpty(serverFolder))
                    return HResults.E_FAIL;

                // Verify the item is under our sync root
                if (!destPath.StartsWith(clientFolder, StringComparison.OrdinalIgnoreCase))
                    return HResults.E_UNEXPECTED;

                // Get the relative path from sync root
                var relativePath = destPath.Substring(clientFolder.Length).TrimStart('\\');

                // Build the path to the source file
                _sourcePath = Path.Combine(serverFolder, relativePath);

                if (!File.Exists(_sourcePath) && !Directory.Exists(_sourcePath))
                    return HResults.E_FAIL;

                // Create a shell item for the source file
                var riid = typeof(IShellItemBase).GUID;
                var hr = ShellNativeMethods.SHCreateItemFromParsingName(
                    _sourcePath,
                    IntPtr.Zero,
                    ref riid,
                    out var sourceItem);

                if (hr < 0)
                    return hr;

                _sourceItem = sourceItem as IShellItemBase;
                return HResults.S_OK;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ThumbnailProvider.Initialize error: {ex.Message}");
                return HResults.E_FAIL;
            }
        }

        /// <summary>
        /// Get the thumbnail by delegating to the source file's thumbnail handler.
        /// </summary>
        public int GetThumbnail(uint cx, out IntPtr phbmp, out WTS_ALPHATYPE pdwAlpha)
        {
            phbmp = IntPtr.Zero;
            pdwAlpha = WTS_ALPHATYPE.WTSAT_UNKNOWN;

            try
            {
                if (_sourceItem == null)
                    return HResults.E_FAIL;

                System.Diagnostics.Trace.WriteLine($"ThumbnailProvider.GetThumbnail: {_sourcePath}, size={cx}");

                // Get the thumbnail handler from the source item
                var bhid = BHID.BHID_ThumbnailHandler;
                var riid = typeof(IThumbnailProvider).GUID;
                var hr = _sourceItem.BindToHandler(
                    IntPtr.Zero,
                    ref bhid,
                    ref riid,
                    out var thumbnailHandler);

                if (hr < 0 || thumbnailHandler == null)
                    return hr;

                var sourceThumbnail = thumbnailHandler as IThumbnailProvider;
                if (sourceThumbnail == null)
                    return HResults.E_NOINTERFACE;

                // Get the thumbnail from the source
                return sourceThumbnail.GetThumbnail(cx, out phbmp, out pdwAlpha);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"ThumbnailProvider.GetThumbnail error: {ex.Message}");
                return HResults.E_FAIL;
            }
        }
    }
}
