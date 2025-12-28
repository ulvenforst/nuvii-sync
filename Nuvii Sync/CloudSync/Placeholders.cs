using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Nuvii_Sync.CloudSync.Native;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Creates placeholder files in the sync root that represent cloud files.
    /// Placeholders consume minimal disk space until hydrated.
    /// Based on CloudMirror sample Placeholders.cpp
    /// </summary>
    public static class Placeholders
    {
        /// <summary>
        /// Creates a single placeholder for a file or folder that was created on the server.
        /// </summary>
        public static bool CreateSingle(string serverPath, string clientPath)
        {
            try
            {
                // Get the parent directories
                var serverParent = Path.GetDirectoryName(serverPath);
                var clientParent = Path.GetDirectoryName(clientPath);
                var fileName = Path.GetFileName(serverPath);

                if (string.IsNullOrEmpty(serverParent) || string.IsNullOrEmpty(clientParent))
                    return false;

                // Check if it already exists
                if (File.Exists(clientPath) || Directory.Exists(clientPath))
                {
                    Trace.WriteLine($"Placeholder already exists: {clientPath}");
                    return false;
                }

                // Ensure parent directory exists
                if (!Directory.Exists(clientParent))
                {
                    Directory.CreateDirectory(clientParent);
                }

                // Get source file info
                var searchPath = serverPath;
                var hFind = CfApi.FindFirstFileW(searchPath, out var findData);
                if (hFind == CfApi.INVALID_HANDLE_VALUE)
                {
                    Trace.WriteLine($"Source not found: {serverPath}");
                    return false;
                }
                CfApi.FindClose(hFind);

                // Build relative name for file identity
                var serverRoot = ProviderFolderLocations.ServerFolder;
                var relativeName = serverPath.StartsWith(serverRoot, StringComparison.OrdinalIgnoreCase)
                    ? serverPath.Substring(serverRoot.Length).TrimStart(Path.DirectorySeparatorChar)
                    : fileName;

                var fileIdentityBytes = System.Text.Encoding.Unicode.GetBytes(relativeName + '\0');
                var fileIdentityHandle = GCHandle.Alloc(fileIdentityBytes, GCHandleType.Pinned);

                try
                {
                    long fileSize = ((long)findData.nFileSizeHigh << 32) + findData.nFileSizeLow;

                    var placeholderInfo = new CF_PLACEHOLDER_CREATE_INFO
                    {
                        RelativeFileName = fileName,
                        FsMetadata = new CF_FS_METADATA
                        {
                            FileSize = fileSize,
                            BasicInfo = new FILE_BASIC_INFO
                            {
                                FileAttributes = findData.dwFileAttributes,
                                CreationTime = FileTimeToLong(findData.ftCreationTime),
                                LastAccessTime = FileTimeToLong(findData.ftLastAccessTime),
                                LastWriteTime = FileTimeToLong(findData.ftLastWriteTime),
                                ChangeTime = FileTimeToLong(findData.ftLastWriteTime)
                            }
                        },
                        FileIdentity = fileIdentityHandle.AddrOfPinnedObject(),
                        FileIdentityLength = (uint)fileIdentityBytes.Length,
                        Flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC
                    };

                    if ((findData.dwFileAttributes & CfApi.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        placeholderInfo.Flags |= CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_DISABLE_ON_DEMAND_POPULATION;
                        placeholderInfo.FsMetadata.FileSize = 0;
                    }

                    var placeholders = new[] { placeholderInfo };
                    var hr = CfApi.CfCreatePlaceholders(
                        clientParent,
                        placeholders,
                        1,
                        CF_CREATE_FLAGS.CF_CREATE_FLAG_NONE,
                        out _);

                    if (hr < 0)
                    {
                        Trace.WriteLine($"Failed to create placeholder {clientPath}: HR=0x{hr:X8}");
                        return false;
                    }

                    Trace.WriteLine($"Created placeholder: {clientPath}");

                    // If it's a directory, recursively create placeholders for contents
                    if ((findData.dwFileAttributes & CfApi.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        Create(ProviderFolderLocations.ServerFolder, relativeName, ProviderFolderLocations.ClientFolder);
                    }

                    return true;
                }
                finally
                {
                    fileIdentityHandle.Free();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error creating single placeholder: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deletes a placeholder from the client folder.
        /// </summary>
        public static bool Delete(string clientPath)
        {
            try
            {
                if (Directory.Exists(clientPath))
                {
                    // Delete directory and all contents
                    Directory.Delete(clientPath, recursive: true);
                    Trace.WriteLine($"Deleted placeholder directory: {clientPath}");
                    return true;
                }
                else if (File.Exists(clientPath))
                {
                    File.Delete(clientPath);
                    Trace.WriteLine($"Deleted placeholder file: {clientPath}");
                    return true;
                }
                else
                {
                    Trace.WriteLine($"Placeholder not found for deletion: {clientPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error deleting placeholder {clientPath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Renames/moves a placeholder in the client folder.
        /// </summary>
        public static bool Rename(string oldClientPath, string newClientPath)
        {
            try
            {
                if (Directory.Exists(oldClientPath))
                {
                    // Ensure parent directory exists
                    var newParent = Path.GetDirectoryName(newClientPath);
                    if (!string.IsNullOrEmpty(newParent) && !Directory.Exists(newParent))
                    {
                        Directory.CreateDirectory(newParent);
                    }

                    Directory.Move(oldClientPath, newClientPath);
                    Trace.WriteLine($"Renamed placeholder directory: {oldClientPath} -> {newClientPath}");
                    return true;
                }
                else if (File.Exists(oldClientPath))
                {
                    // Ensure parent directory exists
                    var newParent = Path.GetDirectoryName(newClientPath);
                    if (!string.IsNullOrEmpty(newParent) && !Directory.Exists(newParent))
                    {
                        Directory.CreateDirectory(newParent);
                    }

                    File.Move(oldClientPath, newClientPath);
                    Trace.WriteLine($"Renamed placeholder file: {oldClientPath} -> {newClientPath}");
                    return true;
                }
                else
                {
                    Trace.WriteLine($"Placeholder not found for rename: {oldClientPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error renaming placeholder {oldClientPath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates placeholders for all files in the source directory.
        /// </summary>
        /// <param name="sourcePath">The server (cloud) folder path.</param>
        /// <param name="sourceSubDir">Relative subdirectory within the source.</param>
        /// <param name="destPath">The client (sync root) folder path.</param>
        public static void Create(string sourcePath, string sourceSubDir, string destPath)
        {
            try
            {
                // Build full paths
                var fullSourcePath = string.IsNullOrEmpty(sourceSubDir)
                    ? sourcePath
                    : Path.Combine(sourcePath, sourceSubDir);

                var fullDestPath = string.IsNullOrEmpty(sourceSubDir)
                    ? destPath
                    : Path.Combine(destPath, sourceSubDir);

                // Ensure destination directory exists
                if (!Directory.Exists(fullDestPath))
                {
                    Directory.CreateDirectory(fullDestPath);
                }

                Trace.WriteLine($"Creating placeholders from: {fullSourcePath}");
                Trace.WriteLine($"To: {fullDestPath}");

                // Find all files in source
                var searchPath = Path.Combine(fullSourcePath, "*");
                var hFind = CfApi.FindFirstFileW(searchPath, out var findData);

                if (hFind == CfApi.INVALID_HANDLE_VALUE)
                {
                    Trace.WriteLine($"No files found in {fullSourcePath}");
                    return;
                }

                try
                {
                    var createdCount = 0;
                    var skippedCount = 0;
                    
                    do
                    {
                        // Skip . and ..
                        if (findData.cFileName == "." || findData.cFileName == "..")
                            continue;

                        // Build relative name for file identity
                        var relativeName = string.IsNullOrEmpty(sourceSubDir)
                            ? findData.cFileName
                            : Path.Combine(sourceSubDir, findData.cFileName);

                        if (CreatePlaceholder(findData, relativeName, fullDestPath, sourcePath, destPath))
                        {
                            createdCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }

                    } while (CfApi.FindNextFileW(hFind, out findData));

                    Trace.WriteLine($"Placeholders: created={createdCount}, skipped={skippedCount}");
                }
                finally
                {
                    CfApi.FindClose(hFind);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error creating placeholders: {ex.Message}");
                throw;
            }
        }

        private static bool CreatePlaceholder(
            WIN32_FIND_DATA findData,
            string relativeName,
            string fullDestPath,
            string sourcePath,
            string destPath)
        {
            try
            {
                // Check if placeholder already exists
                var destFilePath = Path.Combine(fullDestPath, findData.cFileName);
                if (File.Exists(destFilePath) || Directory.Exists(destFilePath))
                {
                    // Already exists, check if it's a directory that needs to be processed
                    if ((findData.dwFileAttributes & CfApi.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        // Recursively process subdirectories even if they exist
                        Create(sourcePath, relativeName, destPath);
                    }
                    return false; // Already exists
                }

                // Create the file identity (used to identify the file in callbacks)
                var fileIdentityBytes = System.Text.Encoding.Unicode.GetBytes(relativeName + '\0');
                var fileIdentityHandle = GCHandle.Alloc(fileIdentityBytes, GCHandleType.Pinned);

                try
                {
                    // Calculate file size
                    long fileSize = ((long)findData.nFileSizeHigh << 32) + findData.nFileSizeLow;

                    // Build placeholder info
                    var placeholderInfo = new CF_PLACEHOLDER_CREATE_INFO
                    {
                        RelativeFileName = findData.cFileName,
                        FsMetadata = new CF_FS_METADATA
                        {
                            FileSize = fileSize,
                            BasicInfo = new FILE_BASIC_INFO
                            {
                                FileAttributes = findData.dwFileAttributes,
                                CreationTime = FileTimeToLong(findData.ftCreationTime),
                                LastAccessTime = FileTimeToLong(findData.ftLastAccessTime),
                                LastWriteTime = FileTimeToLong(findData.ftLastWriteTime),
                                ChangeTime = FileTimeToLong(findData.ftLastWriteTime)
                            }
                        },
                        FileIdentity = fileIdentityHandle.AddrOfPinnedObject(),
                        FileIdentityLength = (uint)fileIdentityBytes.Length,
                        Flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC
                    };

                    // For directories, disable on-demand population and set size to 0
                    if ((findData.dwFileAttributes & CfApi.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        placeholderInfo.Flags |= CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_DISABLE_ON_DEMAND_POPULATION;
                        placeholderInfo.FsMetadata.FileSize = 0;
                    }

                    // Create the placeholder
                    var placeholders = new[] { placeholderInfo };
                    var hr = CfApi.CfCreatePlaceholders(
                        fullDestPath,
                        placeholders,
                        1,
                        CF_CREATE_FLAGS.CF_CREATE_FLAG_NONE,
                        out var entriesProcessed);

                    if (hr < 0)
                    {
                        Trace.WriteLine($"Failed placeholder for {relativeName}: HR=0x{hr:X8}");
                        return false;
                    }

                    Trace.WriteLine($"Created placeholder: {relativeName}");

                    // Recursively create placeholders for subdirectories
                    if ((findData.dwFileAttributes & CfApi.FILE_ATTRIBUTE_DIRECTORY) != 0)
                    {
                        Create(sourcePath, relativeName, destPath);
                    }

                    return true;
                }
                finally
                {
                    fileIdentityHandle.Free();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error placeholder {relativeName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Converts a FILETIME to a long (100-nanosecond intervals since Jan 1, 1601).
        /// </summary>
        private static long FileTimeToLong(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        {
            return ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
        }
    }
}
