using System;
using System.Runtime.InteropServices;

namespace Nuvii_Sync.CloudSync.Native
{
    /// <summary>
    /// Structures and enums for Cloud Filter API (cfapi.h).
    /// Reference: https://learn.microsoft.com/en-us/windows/win32/cfapi/cloud-filter-reference
    /// </summary>

    #region Connection and Registration Types

    /// <summary>
    /// Opaque handle representing a connection to a sync root.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CONNECTION_KEY
    {
        public long Internal;
    }

    /// <summary>
    /// Opaque handle representing a data transfer operation.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_TRANSFER_KEY
    {
        public long Internal;
    }

    /// <summary>
    /// Sync root registration information.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CF_SYNC_REGISTRATION
    {
        public uint StructSize;
        public nint ProviderName;
        public uint ProviderNameLength;
        public nint ProviderVersion;
        public uint ProviderVersionLength;
        public nint SyncRootIdentity;
        public uint SyncRootIdentityLength;
        public nint FileIdentity;
        public uint FileIdentityLength;
        public Guid ProviderId;
    }

    /// <summary>
    /// Sync root policies configuration.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_SYNC_POLICIES
    {
        public uint StructSize;
        public CF_HYDRATION_POLICY Hydration;
        public CF_POPULATION_POLICY Population;
        public CF_INSYNC_POLICY InSync;
        public CF_HARDLINK_POLICY HardLink;
        public CF_PLACEHOLDER_MANAGEMENT_POLICY PlaceholderManagement;
    }

    /// <summary>
    /// Sync status for reporting to the platform.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CF_SYNC_STATUS
    {
        public uint StructSize;
        public uint Code;
        public uint DescriptionOffset;
        public uint DescriptionLength;
        public uint DeviceIdOffset;
        public uint DeviceIdLength;
    }

    #endregion

    #region Policy Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_HYDRATION_POLICY
    {
        public CF_HYDRATION_POLICY_PRIMARY Primary;
        public CF_HYDRATION_POLICY_MODIFIER Modifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_POPULATION_POLICY
    {
        public CF_POPULATION_POLICY_PRIMARY Primary;
        public CF_POPULATION_POLICY_MODIFIER Modifier;
    }

    #endregion

    #region Callback Structures

    /// <summary>
    /// Callback registration entry for sync root callbacks.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_REGISTRATION
    {
        public CF_CALLBACK_TYPE Type;
        public nint Callback; // CF_CALLBACK function pointer

        public static CF_CALLBACK_REGISTRATION CF_CALLBACK_REGISTRATION_END => new()
        {
            Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NONE,
            Callback = nint.Zero
        };
    }

    /// <summary>
    /// Information passed to callbacks about the operation context.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_INFO
    {
        public uint StructSize;
        public CF_CONNECTION_KEY ConnectionKey;
        public nint CallbackContext;
        public nint VolumeGuidName;
        public nint VolumeDosName;
        public uint VolumeSerialNumber;
        public long SyncRootFileId;
        public nint SyncRootIdentity;
        public uint SyncRootIdentityLength;
        public long FileId;
        public long FileSize;
        public nint FileIdentity;
        public uint FileIdentityLength;
        public nint NormalizedPath;
        public CF_TRANSFER_KEY TransferKey;
        public byte PriorityHint;
        public nint CorrelationVector;
        public nint ProcessInfo;
        public long RequestKey;

        public readonly string GetNormalizedPath()
        {
            return NormalizedPath != nint.Zero
                ? Marshal.PtrToStringUni(NormalizedPath) ?? string.Empty
                : string.Empty;
        }

        public readonly string GetVolumeDosName()
        {
            return VolumeDosName != nint.Zero
                ? Marshal.PtrToStringUni(VolumeDosName) ?? string.Empty
                : string.Empty;
        }

        public readonly string GetFullPath()
        {
            return GetVolumeDosName() + GetNormalizedPath();
        }

        public readonly string GetFileIdentity()
        {
            if (FileIdentity == nint.Zero || FileIdentityLength == 0)
                return string.Empty;

            return Marshal.PtrToStringUni(FileIdentity, (int)(FileIdentityLength / sizeof(char)))?.TrimEnd('\0') ?? string.Empty;
        }
    }

    /// <summary>
    /// Callback parameters - this is a union structure in C.
    /// We need separate structs for each callback type with correct alignment.
    /// 
    /// Native layout on x64:
    /// - ParamSize: ULONG (4 bytes) at offset 0
    /// - [padding]: 4 bytes to align union to 8 bytes
    /// - Union members start at offset 8
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS
    {
        public uint ParamSize;
        // This is followed by the specific callback params based on type
        // We'll read it manually based on callback type
    }

    /// <summary>
    /// FetchData callback parameters.
    /// 
    /// Native layout on x64:
    /// - ParamSize: 4 bytes (offset 0)
    /// - [padding]: 4 bytes (offset 4) - align union to 8
    /// - Flags: 4 bytes (offset 8)
    /// - [padding]: 4 bytes (offset 12) - align LARGE_INTEGER to 8
    /// - RequiredFileOffset: 8 bytes (offset 16)
    /// - RequiredLength: 8 bytes (offset 24)
    /// - OptionalFileOffset: 8 bytes (offset 32)
    /// - OptionalLength: 8 bytes (offset 40)
    /// - LastDehydrationTime: 8 bytes (offset 48)
    /// - LastDehydrationReason: 4 bytes (offset 56)
    /// - [padding]: 4 bytes (offset 60) - struct alignment
    /// Total: 64 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS_FETCHDATA
    {
        public uint ParamSize;
        private uint _padding1;  // Align union to 8 bytes
        public CF_CALLBACK_FETCH_DATA_FLAGS Flags;
        private uint _padding2;  // Align LARGE_INTEGER to 8 bytes
        public long RequiredFileOffset;
        public long RequiredLength;
        public long OptionalFileOffset;
        public long OptionalLength;
        public long LastDehydrationTime;
        public CF_CALLBACK_DEHYDRATION_REASON LastDehydrationReason;
        private uint _padding3;  // Struct alignment
    }

    /// <summary>
    /// Cancel callback parameters.
    /// 
    /// Native layout on x64:
    /// - ParamSize: 4 bytes (offset 0)
    /// - [padding]: 4 bytes (offset 4)
    /// - Flags: 4 bytes (offset 8)
    /// - [padding]: 4 bytes (offset 12)
    /// - FetchData.FileOffset: 8 bytes (offset 16)
    /// - FetchData.Length: 8 bytes (offset 24)
    /// Total: 32 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS_CANCEL
    {
        public uint ParamSize;
        private uint _padding1;
        public CF_CALLBACK_CANCEL_FLAGS Flags;
        private uint _padding2;
        public long FileOffset;
        public long Length;
    }

    /// <summary>
    /// Delete completion callback parameters.
    /// 
    /// Native layout on x64:
    /// - ParamSize: 4 bytes (offset 0)
    /// - [padding]: 4 bytes (offset 4)
    /// - Flags: 4 bytes (offset 8)
    /// Total: 16 bytes (with alignment padding)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS_DELETE_COMPLETION
    {
        public uint ParamSize;
        private uint _padding1;
        public CF_CALLBACK_DELETE_COMPLETION_FLAGS Flags;
    }

    /// <summary>
    /// Rename callback parameters (pre-rename notification).
    /// Called BEFORE the rename - gives old path in callbackInfo, new path here.
    /// 
    /// Native layout on x64:
    /// - ParamSize: 4 bytes (offset 0)
    /// - [padding]: 4 bytes (offset 4)
    /// - Flags: 4 bytes (offset 8)
    /// - [padding]: 4 bytes (offset 12) - align pointer to 8
    /// - TargetPath: 8 bytes (offset 16)
    /// Total: 24 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS_RENAME
    {
        public uint ParamSize;
        private uint _padding1;
        public CF_CALLBACK_RENAME_FLAGS Flags;
        private uint _padding2;
        public nint TargetPath; // PCWSTR - the new path after rename
    }

    /// <summary>
    /// Rename completion callback parameters (post-rename notification).
    /// Called AFTER the rename - gives new path in callbackInfo, old path here.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS_RENAME_COMPLETION
    {
        public uint ParamSize;
        private uint _padding1;
        public CF_CALLBACK_RENAME_COMPLETION_FLAGS Flags;
        private uint _padding2;
        public nint SourcePath; // PCWSTR - the original path before rename
    }

    /// <summary>
    /// Close completion callback parameters.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS_CLOSE_COMPLETION
    {
        public uint ParamSize;
        private uint _padding1;
        public CF_CALLBACK_CLOSE_COMPLETION_FLAGS Flags;
    }

    /// <summary>
    /// Process information for callbacks.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_PROCESS_INFO
    {
        public uint StructSize;
        public uint ProcessId;
        public nint ImagePath;
        public nint PackageName;
        public nint ApplicationId;
        public nint CommandLine;
        public uint SessionId;
    }

    #endregion

    #region Operation Structures

    /// <summary>
    /// Operation info for CfExecute.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_INFO
    {
        public uint StructSize;
        public CF_OPERATION_TYPE Type;
        public CF_CONNECTION_KEY ConnectionKey;
        public CF_TRANSFER_KEY TransferKey;
        public nint CorrelationVector;
        public nint SyncStatus;
        public long RequestKey;

        public static CF_OPERATION_INFO Create(
            CF_OPERATION_TYPE type,
            in CF_CALLBACK_INFO callbackInfo)
        {
            return new CF_OPERATION_INFO
            {
                StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
                Type = type,
                ConnectionKey = callbackInfo.ConnectionKey,
                TransferKey = callbackInfo.TransferKey,
                CorrelationVector = callbackInfo.CorrelationVector,
                RequestKey = callbackInfo.RequestKey
            };
        }
    }

    /// <summary>
    /// Operation parameters for CfExecute - Transfer Data.
    /// The ParamSize should be calculated to include only up to the TransferData field.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_PARAMETERS
    {
        public uint ParamSize;
        public CF_OPERATION_TRANSFER_DATA_PARAMS TransferData;

        public static uint GetTransferDataParamSize()
        {
            // ParamSize should be offset of TransferData + size of TransferData
            return (uint)Marshal.SizeOf<CF_OPERATION_PARAMETERS>();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_TRANSFER_DATA_PARAMS
    {
        public CF_OPERATION_TRANSFER_DATA_FLAGS Flags;
        public int CompletionStatus;
        public nint Buffer;
        public long Offset;
        public long Length;
    }

    /// <summary>
    /// Operation parameters for ACK_RENAME.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_PARAMETERS_ACK_RENAME
    {
        public uint ParamSize;
        public CF_OPERATION_ACK_RENAME_FLAGS Flags;
        public int CompletionStatus;
    }

    /// <summary>
    /// Operation parameters for ACK_DELETE.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_PARAMETERS_ACK_DELETE
    {
        public uint ParamSize;
        public CF_OPERATION_ACK_DELETE_FLAGS Flags;
        public int CompletionStatus;
    }

    #endregion

    #region Placeholder Structures

    /// <summary>
    /// Information for creating a placeholder file or directory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CF_PLACEHOLDER_CREATE_INFO
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string RelativeFileName;
        public CF_FS_METADATA FsMetadata;
        public nint FileIdentity;
        public uint FileIdentityLength;
        public CF_PLACEHOLDER_CREATE_FLAGS Flags;
        public int Result;
        public long CreateUsn;
    }

    /// <summary>
    /// File system metadata for placeholders.
    /// Must match native CF_FS_METADATA layout exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_FS_METADATA
    {
        public FILE_BASIC_INFO BasicInfo;  // 40 bytes (5 x 8-byte values)
        public long FileSize;              // 8 bytes
    }

    /// <summary>
    /// Basic file information.
    /// Must match native FILE_BASIC_INFO layout exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FILE_BASIC_INFO
    {
        public long CreationTime;      // LARGE_INTEGER
        public long LastAccessTime;    // LARGE_INTEGER
        public long LastWriteTime;     // LARGE_INTEGER
        public long ChangeTime;        // LARGE_INTEGER
        public uint FileAttributes;    // DWORD
        private uint _padding;         // Alignment padding to 8 bytes
    }

    /// <summary>
    /// File range for dehydration operations.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CF_FILE_RANGE
    {
        public long StartingOffset;
        public long Length;
    }

    #endregion

    #region Platform Info

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_PLATFORM_INFO
    {
        public uint BuildNumber;
        public uint RevisionNumber;
        public uint IntegrationNumber;
    }

    #endregion

    #region Enums

    public enum CF_CALLBACK_TYPE : uint
    {
        CF_CALLBACK_TYPE_FETCH_DATA = 0,
        CF_CALLBACK_TYPE_VALIDATE_DATA = 1,
        CF_CALLBACK_TYPE_CANCEL_FETCH_DATA = 2,
        CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS = 3,
        CF_CALLBACK_TYPE_CANCEL_FETCH_PLACEHOLDERS = 4,
        CF_CALLBACK_TYPE_NOTIFY_FILE_OPEN_COMPLETION = 5,
        CF_CALLBACK_TYPE_NOTIFY_FILE_CLOSE_COMPLETION = 6,
        CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE = 7,
        CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE_COMPLETION = 8,
        CF_CALLBACK_TYPE_NOTIFY_DELETE = 9,
        CF_CALLBACK_TYPE_NOTIFY_DELETE_COMPLETION = 10,
        CF_CALLBACK_TYPE_NOTIFY_RENAME = 11,
        CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION = 12,
        CF_CALLBACK_TYPE_NONE = 0xFFFFFFFF
    }

    public enum CF_OPERATION_TYPE : uint
    {
        CF_OPERATION_TYPE_TRANSFER_DATA = 0,
        CF_OPERATION_TYPE_RETRIEVE_DATA = 1,
        CF_OPERATION_TYPE_ACK_DATA = 2,
        CF_OPERATION_TYPE_RESTART_HYDRATION = 3,
        CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS = 4,
        CF_OPERATION_TYPE_ACK_DEHYDRATE = 5,
        CF_OPERATION_TYPE_ACK_DELETE = 6,
        CF_OPERATION_TYPE_ACK_RENAME = 7
    }

    [Flags]
    public enum CF_REGISTER_FLAGS : uint
    {
        CF_REGISTER_FLAG_NONE = 0,
        CF_REGISTER_FLAG_UPDATE = 1,
        CF_REGISTER_FLAG_DISABLE_ON_DEMAND_POPULATION_ON_ROOT = 2,
        CF_REGISTER_FLAG_MARK_IN_SYNC_ON_ROOT = 4
    }

    [Flags]
    public enum CF_CONNECT_FLAGS : uint
    {
        CF_CONNECT_FLAG_NONE = 0,
        CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO = 2,
        CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH = 4,
        CF_CONNECT_FLAG_BLOCK_SELF_IMPLICIT_HYDRATION = 8
    }

    [Flags]
    public enum CF_CREATE_FLAGS : uint
    {
        CF_CREATE_FLAG_NONE = 0,
        CF_CREATE_FLAG_STOP_ON_ERROR = 1
    }

    [Flags]
    public enum CF_PLACEHOLDER_CREATE_FLAGS : uint
    {
        CF_PLACEHOLDER_CREATE_FLAG_NONE = 0,
        CF_PLACEHOLDER_CREATE_FLAG_DISABLE_ON_DEMAND_POPULATION = 1,
        CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC = 2,
        CF_PLACEHOLDER_CREATE_FLAG_SUPERSEDE = 4,
        CF_PLACEHOLDER_CREATE_FLAG_ALWAYS_FULL = 8
    }

    [Flags]
    public enum CF_CONVERT_FLAGS : uint
    {
        CF_CONVERT_FLAG_NONE = 0,
        CF_CONVERT_FLAG_MARK_IN_SYNC = 1,
        CF_CONVERT_FLAG_DEHYDRATE = 2,
        CF_CONVERT_FLAG_ENABLE_ON_DEMAND_POPULATION = 4,
        CF_CONVERT_FLAG_ALWAYS_FULL = 8,
        CF_CONVERT_FLAG_FORCE_CONVERT_TO_CLOUD_FILE = 16
    }

    [Flags]
    public enum CF_UPDATE_FLAGS : uint
    {
        CF_UPDATE_FLAG_NONE = 0,
        CF_UPDATE_FLAG_VERIFY_IN_SYNC = 1,
        CF_UPDATE_FLAG_MARK_IN_SYNC = 2,
        CF_UPDATE_FLAG_DEHYDRATE = 4,
        CF_UPDATE_FLAG_ENABLE_ON_DEMAND_POPULATION = 8,
        CF_UPDATE_FLAG_DISABLE_ON_DEMAND_POPULATION = 16,
        CF_UPDATE_FLAG_REMOVE_FILE_IDENTITY = 32,
        CF_UPDATE_FLAG_CLEAR_IN_SYNC = 64,
        CF_UPDATE_FLAG_REMOVE_PROPERTY = 128,
        CF_UPDATE_FLAG_PASSTHROUGH_FS_METADATA = 256,
        CF_UPDATE_FLAG_ALWAYS_FULL = 512,
        CF_UPDATE_FLAG_ALLOW_PARTIAL = 1024
    }

    [Flags]
    public enum CF_REVERT_FLAGS : uint
    {
        CF_REVERT_FLAG_NONE = 0
    }

    [Flags]
    public enum CF_HYDRATE_FLAGS : uint
    {
        CF_HYDRATE_FLAG_NONE = 0
    }

    [Flags]
    public enum CF_DEHYDRATE_FLAGS : uint
    {
        CF_DEHYDRATE_FLAG_NONE = 0,
        CF_DEHYDRATE_FLAG_BACKGROUND = 1
    }

    [Flags]
    public enum CF_CALLBACK_FETCH_DATA_FLAGS : uint
    {
        CF_CALLBACK_FETCH_DATA_FLAG_NONE = 0,
        CF_CALLBACK_FETCH_DATA_FLAG_RECOVERY = 1,
        CF_CALLBACK_FETCH_DATA_FLAG_EXPLICIT_HYDRATION = 2
    }

    [Flags]
    public enum CF_CALLBACK_CANCEL_FLAGS : uint
    {
        CF_CALLBACK_CANCEL_FLAG_NONE = 0,
        CF_CALLBACK_CANCEL_FLAG_IO_TIMEOUT = 1,
        CF_CALLBACK_CANCEL_FLAG_IO_ABORTED = 2
    }

    [Flags]
    public enum CF_CALLBACK_DELETE_COMPLETION_FLAGS : uint
    {
        CF_CALLBACK_DELETE_COMPLETION_FLAG_NONE = 0
    }

    [Flags]
    public enum CF_CALLBACK_RENAME_FLAGS : uint
    {
        CF_CALLBACK_RENAME_FLAG_NONE = 0,
        CF_CALLBACK_RENAME_FLAG_IS_DIRECTORY = 1,
        CF_CALLBACK_RENAME_FLAG_SOURCE_IN_SCOPE = 2,
        CF_CALLBACK_RENAME_FLAG_TARGET_IN_SCOPE = 4
    }

    [Flags]
    public enum CF_CALLBACK_RENAME_COMPLETION_FLAGS : uint
    {
        CF_CALLBACK_RENAME_COMPLETION_FLAG_NONE = 0
    }

    [Flags]
    public enum CF_CALLBACK_CLOSE_COMPLETION_FLAGS : uint
    {
        CF_CALLBACK_CLOSE_COMPLETION_FLAG_NONE = 0,
        CF_CALLBACK_CLOSE_COMPLETION_FLAG_DELETED = 1
    }

    public enum CF_CALLBACK_DEHYDRATION_REASON : uint
    {
        CF_CALLBACK_DEHYDRATION_REASON_NONE = 0,
        CF_CALLBACK_DEHYDRATION_REASON_USER_MANUAL = 1,
        CF_CALLBACK_DEHYDRATION_REASON_SYSTEM_LOW_SPACE = 2,
        CF_CALLBACK_DEHYDRATION_REASON_SYSTEM_INACTIVITY = 3,
        CF_CALLBACK_DEHYDRATION_REASON_SYSTEM_OS_UPGRADE = 4
    }

    [Flags]
    public enum CF_OPERATION_TRANSFER_DATA_FLAGS : uint
    {
        CF_OPERATION_TRANSFER_DATA_FLAG_NONE = 0
    }

    [Flags]
    public enum CF_OPERATION_ACK_RENAME_FLAGS : uint
    {
        CF_OPERATION_ACK_RENAME_FLAG_NONE = 0
    }

    [Flags]
    public enum CF_OPERATION_ACK_DELETE_FLAGS : uint
    {
        CF_OPERATION_ACK_DELETE_FLAG_NONE = 0
    }

    public enum CF_HYDRATION_POLICY_PRIMARY : ushort
    {
        CF_HYDRATION_POLICY_PARTIAL = 0,
        CF_HYDRATION_POLICY_PROGRESSIVE = 1,
        CF_HYDRATION_POLICY_FULL = 2,
        CF_HYDRATION_POLICY_ALWAYS_FULL = 3
    }

    [Flags]
    public enum CF_HYDRATION_POLICY_MODIFIER : ushort
    {
        CF_HYDRATION_POLICY_MODIFIER_NONE = 0,
        CF_HYDRATION_POLICY_MODIFIER_VALIDATION_REQUIRED = 1,
        CF_HYDRATION_POLICY_MODIFIER_STREAMING_ALLOWED = 2,
        CF_HYDRATION_POLICY_MODIFIER_AUTO_DEHYDRATION_ALLOWED = 4,
        CF_HYDRATION_POLICY_MODIFIER_ALLOW_FULL_RESTART_HYDRATION = 8
    }

    public enum CF_POPULATION_POLICY_PRIMARY : ushort
    {
        CF_POPULATION_POLICY_PARTIAL = 0,
        CF_POPULATION_POLICY_FULL = 2,
        CF_POPULATION_POLICY_ALWAYS_FULL = 3
    }

    [Flags]
    public enum CF_POPULATION_POLICY_MODIFIER : ushort
    {
        CF_POPULATION_POLICY_MODIFIER_NONE = 0
    }

    [Flags]
    public enum CF_INSYNC_POLICY : uint
    {
        CF_INSYNC_POLICY_NONE = 0,
        CF_INSYNC_POLICY_TRACK_FILE_CREATION_TIME = 1,
        CF_INSYNC_POLICY_TRACK_FILE_READONLY_ATTRIBUTE = 2,
        CF_INSYNC_POLICY_TRACK_FILE_HIDDEN_ATTRIBUTE = 4,
        CF_INSYNC_POLICY_TRACK_FILE_SYSTEM_ATTRIBUTE = 8,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_CREATION_TIME = 16,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_READONLY_ATTRIBUTE = 32,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_HIDDEN_ATTRIBUTE = 64,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_SYSTEM_ATTRIBUTE = 128,
        CF_INSYNC_POLICY_TRACK_FILE_LAST_WRITE_TIME = 256,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_LAST_WRITE_TIME = 512,
        CF_INSYNC_POLICY_PRESERVE_INSYNC_FOR_SYNC_ENGINE = 0x80000000
    }

    [Flags]
    public enum CF_HARDLINK_POLICY : uint
    {
        CF_HARDLINK_POLICY_NONE = 0,
        CF_HARDLINK_POLICY_ALLOWED = 1
    }

    [Flags]
    public enum CF_PLACEHOLDER_MANAGEMENT_POLICY : uint
    {
        CF_PLACEHOLDER_MANAGEMENT_POLICY_DEFAULT = 0,
        CF_PLACEHOLDER_MANAGEMENT_POLICY_CREATE_UNRESTRICTED = 1,
        CF_PLACEHOLDER_MANAGEMENT_POLICY_CONVERT_TO_UNRESTRICTED = 2,
        CF_PLACEHOLDER_MANAGEMENT_POLICY_UPDATE_UNRESTRICTED = 4
    }

    public enum CF_IN_SYNC_STATE : uint
    {
        CF_IN_SYNC_STATE_NOT_IN_SYNC = 0,
        CF_IN_SYNC_STATE_IN_SYNC = 1
    }

    [Flags]
    public enum CF_SET_IN_SYNC_FLAGS : uint
    {
        CF_SET_IN_SYNC_FLAG_NONE = 0
    }

    public enum CF_PIN_STATE : uint
    {
        CF_PIN_STATE_UNSPECIFIED = 0,
        CF_PIN_STATE_PINNED = 1,
        CF_PIN_STATE_UNPINNED = 2,
        CF_PIN_STATE_EXCLUDED = 3,
        CF_PIN_STATE_INHERIT = 4
    }

    [Flags]
    public enum CF_SET_PIN_FLAGS : uint
    {
        CF_SET_PIN_FLAG_NONE = 0,
        CF_SET_PIN_FLAG_RECURSE = 1,
        CF_SET_PIN_FLAG_RECURSE_ONLY = 2,
        CF_SET_PIN_FLAG_RECURSE_STOP_ON_ERROR = 4
    }

    public enum CF_PLACEHOLDER_INFO_CLASS : uint
    {
        CF_PLACEHOLDER_INFO_BASIC = 0,
        CF_PLACEHOLDER_INFO_STANDARD = 1
    }

    public enum CF_SYNC_ROOT_INFO_CLASS : uint
    {
        CF_SYNC_ROOT_INFO_BASIC = 0,
        CF_SYNC_ROOT_INFO_STANDARD = 1,
        CF_SYNC_ROOT_INFO_PROVIDER = 2
    }

    [Flags]
    public enum CF_OPEN_FILE_FLAGS : uint
    {
        CF_OPEN_FILE_FLAG_NONE = 0,
        CF_OPEN_FILE_FLAG_EXCLUSIVE = 1,
        CF_OPEN_FILE_FLAG_WRITE_ACCESS = 2,
        CF_OPEN_FILE_FLAG_DELETE_ACCESS = 4,
        CF_OPEN_FILE_FLAG_FOREGROUND = 8
    }

    [Flags]
    public enum CF_PLACEHOLDER_STATE : uint
    {
        CF_PLACEHOLDER_STATE_NO_STATES = 0,
        CF_PLACEHOLDER_STATE_PLACEHOLDER = 1,
        CF_PLACEHOLDER_STATE_SYNC_ROOT = 2,
        CF_PLACEHOLDER_STATE_ESSENTIAL_PROP_PRESENT = 4,
        CF_PLACEHOLDER_STATE_IN_SYNC = 8,
        CF_PLACEHOLDER_STATE_PARTIAL = 16,
        CF_PLACEHOLDER_STATE_PARTIALLY_ON_DISK = 32,
        CF_PLACEHOLDER_STATE_INVALID = 0xFFFFFFFF
    }

    public enum CF_SYNC_PROVIDER_STATUS : uint
    {
        CF_PROVIDER_STATUS_DISCONNECTED = 0,
        CF_PROVIDER_STATUS_IDLE = 1,
        CF_PROVIDER_STATUS_POPULATE_NAMESPACE = 2,
        CF_PROVIDER_STATUS_POPULATE_METADATA = 4,
        CF_PROVIDER_STATUS_POPULATE_CONTENT = 8,
        CF_PROVIDER_STATUS_SYNC_INCREMENTAL = 16,
        CF_PROVIDER_STATUS_SYNC_FULL = 32,
        CF_PROVIDER_STATUS_CONNECTIVITY_LOST = 64,
        CF_PROVIDER_STATUS_CLEAR_FLAGS = 0x80000000,
        CF_PROVIDER_STATUS_TERMINATED = 0xC0000001,
        CF_PROVIDER_STATUS_ERROR = 0xC0000002
    }

    #endregion

    #region Callback Delegate

    /// <summary>
    /// Callback function signature for sync root operations.
    /// IMPORTANT: The second parameter is a pointer to CF_CALLBACK_PARAMETERS,
    /// which is a union. You must read it based on the callback type.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void CF_CALLBACK(
        in CF_CALLBACK_INFO callbackInfo,
        nint callbackParameters);

    #endregion
}
