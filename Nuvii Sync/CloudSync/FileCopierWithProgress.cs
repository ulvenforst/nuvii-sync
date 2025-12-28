using System;
using System.IO;
using System.Runtime.InteropServices;
using Nuvii_Sync.CloudSync.Native;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Handles copying file data from the server to the client with progress reporting.
    /// Based on CloudMirror sample FileCopierWithProgress.cpp
    /// </summary>
    public static class FileCopierWithProgress
    {
        // Use larger chunks for better performance (64KB like CloudMirror)
        private const int CHUNK_SIZE = 64 * 1024;

        /// <summary>
        /// Copies data from the server file to the placeholder file with progress reporting.
        /// Called in response to CF_CALLBACK_TYPE_FETCH_DATA.
        /// </summary>
        public static void CopyFromServerToClient(
            in CF_CALLBACK_INFO callbackInfo,
            in CF_CALLBACK_PARAMETERS_FETCHDATA fetchParams,
            string serverFolder)
        {
            var connectionKey = callbackInfo.ConnectionKey;
            var transferKey = callbackInfo.TransferKey;
            var correlationVector = callbackInfo.CorrelationVector;
            var requestKey = callbackInfo.RequestKey;
            var fileIdentity = callbackInfo.GetFileIdentity();
            var fullPath = callbackInfo.GetFullPath();

            var requiredOffset = fetchParams.RequiredFileOffset;
            var requiredLength = fetchParams.RequiredLength;

            Trace.WriteLine($"CopyFromServerToClient: {fullPath}");
            Trace.WriteLine($"  FileIdentity: '{fileIdentity}'");
            Trace.WriteLine($"  Offset: {requiredOffset}, Length: {requiredLength}");

            try
            {
                if (string.IsNullOrEmpty(fileIdentity))
                {
                    Trace.WriteLine("ERROR: No file identity!");
                    TransferData(connectionKey, transferKey, correlationVector, requestKey,
                        nint.Zero, requiredOffset, requiredLength, STATUS_UNSUCCESSFUL);
                    return;
                }

                var serverFilePath = Path.Combine(serverFolder, fileIdentity);
                Trace.WriteLine($"  Server file: {serverFilePath}");

                if (!File.Exists(serverFilePath))
                {
                    Trace.WriteLine($"ERROR: Server file not found: {serverFilePath}");
                    TransferData(connectionKey, transferKey, correlationVector, requestKey,
                        nint.Zero, requiredOffset, requiredLength, STATUS_OBJECT_NAME_NOT_FOUND);
                    return;
                }

                TransferFileData(connectionKey, transferKey, correlationVector, requestKey,
                    serverFilePath, requiredOffset, requiredLength);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ERROR in CopyFromServerToClient: {ex.Message}");

                try
                {
                    TransferData(connectionKey, transferKey, correlationVector, requestKey,
                        nint.Zero, requiredOffset, requiredLength, STATUS_UNSUCCESSFUL);
                }
                catch { }
            }
        }

        private static void TransferFileData(
            CF_CONNECTION_KEY connectionKey,
            CF_TRANSFER_KEY transferKey,
            nint correlationVector,
            long requestKey,
            string serverFilePath,
            long requiredOffset,
            long requiredLength)
        {
            try
            {
                using var serverStream = new FileStream(
                    serverFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    CHUNK_SIZE);

                serverStream.Seek(requiredOffset, SeekOrigin.Begin);

                var buffer = new byte[CHUNK_SIZE];
                var totalToTransfer = requiredLength;
                var totalTransferred = 0L;
                var currentOffset = requiredOffset;

                while (totalTransferred < totalToTransfer)
                {
                    var chunkSize = (int)Math.Min(CHUNK_SIZE, totalToTransfer - totalTransferred);
                    var bytesRead = serverStream.Read(buffer, 0, chunkSize);

                    if (bytesRead == 0)
                    {
                        Trace.WriteLine($"  EOF at offset {currentOffset}");
                        break;
                    }

                    var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                    try
                    {
                        // Transfer the data chunk
                        TransferData(connectionKey, transferKey, correlationVector, requestKey,
                            bufferHandle.AddrOfPinnedObject(), currentOffset, bytesRead, STATUS_SUCCESS);
                    }
                    finally
                    {
                        bufferHandle.Free();
                    }

                    totalTransferred += bytesRead;
                    currentOffset += bytesRead;

                    // Report progress to show in File Explorer
                    ReportProgress(connectionKey, transferKey, totalToTransfer, totalTransferred);
                }

                Trace.WriteLine($"  Transfer complete: {totalTransferred} bytes");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"ERROR in TransferFileData: {ex.Message}");
                TransferData(connectionKey, transferKey, correlationVector, requestKey,
                    nint.Zero, requiredOffset, requiredLength, STATUS_UNSUCCESSFUL);
            }
        }

        /// <summary>
        /// Reports transfer progress to File Explorer.
        /// </summary>
        private static void ReportProgress(
            CF_CONNECTION_KEY connectionKey,
            CF_TRANSFER_KEY transferKey,
            long total,
            long completed)
        {
            try
            {
                CfApi.CfReportProviderProgress(connectionKey, transferKey, total, completed);
            }
            catch
            {
                // Progress reporting is optional, don't fail the transfer
            }
        }

        /// <summary>
        /// Called when a fetch operation is cancelled.
        /// </summary>
        public static void CancelCopyFromServerToClient(
            in CF_CALLBACK_INFO callbackInfo,
            in CF_CALLBACK_PARAMETERS_CANCEL cancelParams)
        {
            Trace.WriteLine($"Cancel: {callbackInfo.GetFullPath()}");
            // In a real implementation, you would track ongoing transfers
            // and cancel them when this callback is received
        }

        /// <summary>
        /// Transfers a chunk of data to the placeholder file using CfExecute.
        /// </summary>
        private static void TransferData(
            CF_CONNECTION_KEY connectionKey,
            CF_TRANSFER_KEY transferKey,
            nint correlationVector,
            long requestKey,
            nint buffer,
            long offset,
            long length,
            int completionStatus)
        {
            var opInfo = new CF_OPERATION_INFO
            {
                StructSize = (uint)Marshal.SizeOf<CF_OPERATION_INFO>(),
                Type = CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA,
                ConnectionKey = connectionKey,
                TransferKey = transferKey,
                CorrelationVector = correlationVector,
                RequestKey = requestKey
            };

            var opParams = new CF_OPERATION_PARAMETERS
            {
                ParamSize = CF_OPERATION_PARAMETERS.GetTransferDataParamSize(),
                TransferData = new CF_OPERATION_TRANSFER_DATA_PARAMS
                {
                    Flags = CF_OPERATION_TRANSFER_DATA_FLAGS.CF_OPERATION_TRANSFER_DATA_FLAG_NONE,
                    CompletionStatus = completionStatus,
                    Buffer = buffer,
                    Offset = offset,
                    Length = length
                }
            };

            var hr = CfApi.CfExecute(opInfo, ref opParams);
            if (hr < 0)
            {
                Trace.WriteLine($"  CfExecute FAILED: HR=0x{hr:X8}");
                Marshal.ThrowExceptionForHR(hr);
            }
        }

        // NTSTATUS codes
        private const int STATUS_SUCCESS = 0;
        private const int STATUS_UNSUCCESSFUL = unchecked((int)0xC0000001);
        private const int STATUS_OBJECT_NAME_NOT_FOUND = unchecked((int)0xC0000034);
    }
}
