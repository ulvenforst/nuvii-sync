using System;
using System.Threading;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Types of sync operations.
    /// </summary>
    public enum SyncOperationType
    {
        Create,
        Rename,
        Delete,
        Modified
    }

    /// <summary>
    /// States of a sync operation.
    /// </summary>
    public enum OperationState
    {
        Pending,
        InProgress,
        Completed,
        Failed
    }

    /// <summary>
    /// Represents a pending sync operation.
    /// </summary>
    public class PendingOperation
    {
        public required string CurrentPath { get; set; }
        public string? OriginalPath { get; set; }
        public required string RelativePath { get; set; }
        public string? OriginalRelativePath { get; set; }
        public SyncOperationType Type { get; set; }
        public OperationState State { get; set; }
        public DateTime CreatedAt { get; set; }
        public CancellationTokenSource? TimerCts { get; set; }
        public bool IsDirectory { get; set; }
    }

    /// <summary>
    /// Event args for completed sync operations.
    /// </summary>
    public class SyncEventArgs : EventArgs
    {
        public PendingOperation Operation { get; }

        public SyncEventArgs(PendingOperation operation)
        {
            Operation = operation;
        }
    }

    /// <summary>
    /// Event args for failed sync operations.
    /// </summary>
    public class SyncErrorEventArgs : SyncEventArgs
    {
        public Exception? Exception { get; }

        public SyncErrorEventArgs(PendingOperation operation, Exception? exception)
            : base(operation)
        {
            Exception = exception;
        }
    }

    /// <summary>
    /// Tracks information about a recently deleted file for Move detection.
    /// Used to convert Delete+Create sequences into Move operations.
    /// </summary>
    internal class DeletedFileInfo
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime DeletedAt { get; set; }
        public bool IsDirectory { get; set; }
    }
}
