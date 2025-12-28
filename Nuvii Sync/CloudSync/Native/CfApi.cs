using System;
using System.Runtime.InteropServices;

namespace Nuvii_Sync.CloudSync.Native
{
    /// <summary>
    /// P/Invoke declarations for the Cloud Filter API (cfapi.h).
    /// Reference: https://learn.microsoft.com/en-us/windows/win32/cfapi/cloud-filter-reference
    /// </summary>
    internal static class CfApi
    {
        private const string CldApiDll = "cldapi.dll";

        #region Sync Root Registration

        /// <summary>
        /// Performs a one time sync root registration.
        /// </summary>
        [DllImport(CldApiDll, CharSet = CharSet.Unicode)]
        public static extern int CfRegisterSyncRoot(
            string syncRootPath,
            in CF_SYNC_REGISTRATION registration,
            in CF_SYNC_POLICIES policies,
            CF_REGISTER_FLAGS registerFlags);

        /// <summary>
        /// Unregisters a previously registered sync root.
        /// </summary>
        [DllImport(CldApiDll, CharSet = CharSet.Unicode)]
        public static extern int CfUnregisterSyncRoot(string syncRootPath);

        #endregion

        #region Connection Management

        /// <summary>
        /// Initiates bi-directional communication between a sync provider and the sync filter API.
        /// </summary>
        [DllImport(CldApiDll, CharSet = CharSet.Unicode)]
        public static extern int CfConnectSyncRoot(
            string syncRootPath,
            [In] CF_CALLBACK_REGISTRATION[] callbackTable,
            nint callbackContext,
            CF_CONNECT_FLAGS connectFlags,
            out CF_CONNECTION_KEY connectionKey);

        /// <summary>
        /// Disconnects a communication channel created by CfConnectSyncRoot.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfDisconnectSyncRoot(CF_CONNECTION_KEY connectionKey);

        #endregion

        #region Placeholder Management

        /// <summary>
        /// Creates one or more new placeholder files or directories under a sync root tree.
        /// </summary>
        [DllImport(CldApiDll, CharSet = CharSet.Unicode)]
        public static extern int CfCreatePlaceholders(
            string baseDirectoryPath,
            [In, Out] CF_PLACEHOLDER_CREATE_INFO[] placeholderArray,
            uint count,
            CF_CREATE_FLAGS createFlags,
            out uint entriesProcessed);

        /// <summary>
        /// Converts a normal file/directory to a placeholder file/directory.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfConvertToPlaceholder(
            nint fileHandle,
            nint fileIdentity,
            uint fileIdentityLength,
            CF_CONVERT_FLAGS convertFlags,
            out long usn,
            nint overlapped);

        /// <summary>
        /// Updates characteristics of the placeholder file or directory.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfUpdatePlaceholder(
            nint fileHandle,
            in CF_FS_METADATA fsMetadata,
            nint fileIdentity,
            uint fileIdentityLength,
            nint dehydrateRangeArray,
            uint dehydrateRangeCount,
            CF_UPDATE_FLAGS updateFlags,
            out long usn,
            nint overlapped);

        /// <summary>
        /// Reverts a placeholder back to a regular file.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfRevertPlaceholder(
            nint fileHandle,
            CF_REVERT_FLAGS revertFlags,
            nint overlapped);

        #endregion

        #region Hydration Operations

        /// <summary>
        /// Hydrates a placeholder file by ensuring that the specified byte range is present on-disk.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfHydratePlaceholder(
            nint fileHandle,
            long startingOffset,
            long length,
            CF_HYDRATE_FLAGS hydrateFlags,
            nint overlapped);

        /// <summary>
        /// Dehydrates a placeholder file, releasing the data on disk while keeping the metadata.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfDehydratePlaceholder(
            nint fileHandle,
            long startingOffset,
            long length,
            CF_DEHYDRATE_FLAGS dehydrateFlags,
            nint overlapped);

        #endregion

        #region Transfer Operations

        /// <summary>
        /// Performs a main operation from a sync provider.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfExecute(
            in CF_OPERATION_INFO opInfo,
            ref CF_OPERATION_PARAMETERS opParams);

        /// <summary>
        /// Performs ACK_RENAME operation from a sync provider.
        /// </summary>
        [DllImport(CldApiDll, EntryPoint = "CfExecute")]
        public static extern int CfExecute(
            in CF_OPERATION_INFO opInfo,
            ref CF_OPERATION_PARAMETERS_ACK_RENAME opParams);

        /// <summary>
        /// Performs ACK_DELETE operation from a sync provider.
        /// </summary>
        [DllImport(CldApiDll, EntryPoint = "CfExecute")]
        public static extern int CfExecute(
            in CF_OPERATION_INFO opInfo,
            ref CF_OPERATION_PARAMETERS_ACK_DELETE opParams);

        /// <summary>
        /// Allows a sync provider to report progress out-of-band.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfReportProviderProgress(
            CF_CONNECTION_KEY connectionKey,
            CF_TRANSFER_KEY transferKey,
            long providerProgressTotal,
            long providerProgressCompleted);

        /// <summary>
        /// Allows a sync provider to report progress out-of-band (version 2).
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfReportProviderProgress2(
            CF_CONNECTION_KEY connectionKey,
            CF_TRANSFER_KEY transferKey,
            long requestKey,
            long providerProgressTotal,
            long providerProgressCompleted,
            uint targetSessionId);

        #endregion

        #region Placeholder State

        /// <summary>
        /// Sets the in-sync state for a placeholder file or folder.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfSetInSyncState(
            nint fileHandle,
            CF_IN_SYNC_STATE inSyncState,
            CF_SET_IN_SYNC_FLAGS inSyncFlags,
            out long usn);

        /// <summary>
        /// Sets the pin state of a placeholder.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfSetPinState(
            nint fileHandle,
            CF_PIN_STATE pinState,
            CF_SET_PIN_FLAGS pinFlags,
            nint overlapped);

        /// <summary>
        /// Gets the platform version information.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfGetPlatformInfo(out CF_PLATFORM_INFO platformInfo);

        /// <summary>
        /// Initiates a transfer of data into a placeholder file or folder.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfGetTransferKey(nint fileHandle, out CF_TRANSFER_KEY transferKey);

        /// <summary>
        /// Releases a transfer key obtained by CfGetTransferKey.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern void CfReleaseTransferKey(nint fileHandle, ref CF_TRANSFER_KEY transferKey);

        /// <summary>
        /// Gets various characteristics of the sync root containing a given file.
        /// </summary>
        [DllImport(CldApiDll, CharSet = CharSet.Unicode)]
        public static extern int CfGetSyncRootInfoByPath(
            string filePath,
            CF_SYNC_ROOT_INFO_CLASS infoClass,
            nint infoBuffer,
            uint infoBufferLength,
            out uint returnedLength);

        /// <summary>
        /// Gets various characteristics of the sync root by handle.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfGetSyncRootInfoByHandle(
            nint fileHandle,
            CF_SYNC_ROOT_INFO_CLASS infoClass,
            nint infoBuffer,
            uint infoBufferLength,
            out uint returnedLength);

        /// <summary>
        /// Gets placeholder information for a file or directory.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfGetPlaceholderInfo(
            nint fileHandle,
            CF_PLACEHOLDER_INFO_CLASS infoClass,
            nint infoBuffer,
            uint infoBufferLength,
            out uint returnedLength);

        /// <summary>
        /// Gets placeholder state from file attributes/reparse tag.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern CF_PLACEHOLDER_STATE CfGetPlaceholderStateFromAttributeTag(
            uint fileAttributes,
            uint reparseTag);

        /// <summary>
        /// Opens an asynchronous opaque handle to a file or directory.
        /// </summary>
        [DllImport(CldApiDll, CharSet = CharSet.Unicode)]
        public static extern int CfOpenFileWithOplock(
            string filePath,
            CF_OPEN_FILE_FLAGS flags,
            out nint protectedHandle);

        /// <summary>
        /// Closes a protected handle opened by CfOpenFileWithOplock.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern void CfCloseHandle(nint fileHandle);

        /// <summary>
        /// Converts a protected handle to a Win32 handle.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern nint CfGetWin32HandleFromProtectedHandle(nint protectedHandle);

        /// <summary>
        /// Allows a sync provider to report sync status.
        /// </summary>
        [DllImport(CldApiDll, CharSet = CharSet.Unicode)]
        public static extern int CfReportSyncStatus(
            string syncRootPath,
            in CF_SYNC_STATUS syncStatus);

        /// <summary>
        /// Updates the current status of the sync provider.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfUpdateSyncProviderStatus(
            CF_CONNECTION_KEY connectionKey,
            CF_SYNC_PROVIDER_STATUS providerStatus);

        /// <summary>
        /// Sets a correlation vector for telemetry purposes.
        /// </summary>
        [DllImport(CldApiDll)]
        public static extern int CfSetCorrelationVector(
            nint fileHandle,
            in Guid correlationVector);

        #endregion

        #region Win32 Helpers

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern nint CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            nint lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            nint hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern nint FindFirstFileW(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindNextFileW(nint hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindClose(nint hFindFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetFileAttributesW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadFile(
            nint hFile,
            nint lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            nint lpOverlapped);

        // File access constants
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint FILE_SHARE_DELETE = 0x00000004;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const uint FILE_ATTRIBUTE_PINNED = 0x00080000;
        public const uint FILE_ATTRIBUTE_UNPINNED = 0x00100000;
        public const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        public static readonly nint INVALID_HANDLE_VALUE = new(-1);

        // Reparse tag for cloud files
        public const uint IO_REPARSE_TAG_CLOUD = 0x9000001A;
        public const uint IO_REPARSE_TAG_CLOUD_1 = 0x9000101A;
        public const uint IO_REPARSE_TAG_CLOUD_2 = 0x9000201A;
        public const uint IO_REPARSE_TAG_CLOUD_3 = 0x9000301A;
        public const uint IO_REPARSE_TAG_CLOUD_4 = 0x9000401A;
        public const uint IO_REPARSE_TAG_CLOUD_5 = 0x9000501A;
        public const uint IO_REPARSE_TAG_CLOUD_6 = 0x9000601A;
        public const uint IO_REPARSE_TAG_CLOUD_7 = 0x9000701A;
        public const uint IO_REPARSE_TAG_CLOUD_8 = 0x9000801A;
        public const uint IO_REPARSE_TAG_CLOUD_9 = 0x9000901A;
        public const uint IO_REPARSE_TAG_CLOUD_A = 0x9000A01A;
        public const uint IO_REPARSE_TAG_CLOUD_B = 0x9000B01A;
        public const uint IO_REPARSE_TAG_CLOUD_C = 0x9000C01A;
        public const uint IO_REPARSE_TAG_CLOUD_D = 0x9000D01A;
        public const uint IO_REPARSE_TAG_CLOUD_E = 0x9000E01A;
        public const uint IO_REPARSE_TAG_CLOUD_F = 0x9000F01A;

        #endregion
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }
}
