using System;
using System.IO;
using System.Text.RegularExpressions;
using Trace = System.Diagnostics.Trace;

namespace Nuvii_Sync.CloudSync
{
    /// <summary>
    /// Filters temporary files to prevent them from syncing.
    /// Uses a 3-layer detection strategy:
    /// 1. FILE_ATTRIBUTE_TEMPORARY - Windows attribute set by well-behaved apps
    /// 2. Known patterns - For apps that don't set the attribute (Office, LibreOffice, Blender, etc.)
    /// 3. Heuristics - For edge cases like Excel hex temp files
    /// </summary>
    public static class TempFileFilter
    {
        // FILE_ATTRIBUTE_TEMPORARY = 0x100
        private const uint FILE_ATTRIBUTE_TEMPORARY = 0x100;

        // Regex for Excel hex temp files (8 hex chars, no extension)
        // e.g., "Cedd4100", "AB3F1200"
        private static readonly Regex HexTempFileRegex = new(
            @"^[A-Fa-f0-9]{8}$",
            RegexOptions.Compiled);

        /// <summary>
        /// Determines if a file should be ignored from sync operations.
        /// Returns true for temporary files, lock files, and backup files.
        /// Use this for existing files where we can check attributes.
        /// </summary>
        public static bool ShouldIgnore(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                var fileName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(fileName))
                    return false;

                // Layer 1: Check Windows FILE_ATTRIBUTE_TEMPORARY
                if (HasTemporaryAttribute(path))
                {
                    Trace.WriteLine($"[TempFilter] Ignored (TEMP attr): {fileName}");
                    return true;
                }

                // Layer 2: Check known patterns
                if (MatchesKnownPattern(fileName))
                {
                    Trace.WriteLine($"[TempFilter] Ignored (pattern): {fileName}");
                    return true;
                }

                // Layer 3: Heuristics for edge cases
                if (MatchesHeuristics(fileName, path))
                {
                    Trace.WriteLine($"[TempFilter] Ignored (heuristic): {fileName}");
                    return true;
                }

                return false;
            }
            catch
            {
                // If we can't determine, don't ignore (safer)
                return false;
            }
        }

        /// <summary>
        /// Determines if a deleted file should be ignored based on its name only.
        /// Use this for deleted files where we can't check attributes.
        /// </summary>
        public static bool ShouldIgnoreByName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                var fileName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(fileName))
                    return false;

                // Check patterns and heuristics (can't check attributes for deleted files)
                if (MatchesKnownPattern(fileName))
                {
                    Trace.WriteLine($"[TempFilter] Ignored deleted (pattern): {fileName}");
                    return true;
                }

                // Check hex temp file pattern
                if (IsHexTempFile(fileName))
                {
                    Trace.WriteLine($"[TempFilter] Ignored deleted (heuristic): {fileName}");
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #region Layer 1: FILE_ATTRIBUTE_TEMPORARY

        /// <summary>
        /// Checks if the file has FILE_ATTRIBUTE_TEMPORARY set.
        /// This is the most universal way to detect temp files.
        /// </summary>
        private static bool HasTemporaryAttribute(string path)
        {
            try
            {
                // File must exist to check attributes
                if (!File.Exists(path))
                    return false;

                var attrs = File.GetAttributes(path);
                return ((uint)attrs & FILE_ATTRIBUTE_TEMPORARY) != 0;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Layer 2: Known Patterns

        /// <summary>
        /// Checks if the filename matches known temporary file patterns.
        /// </summary>
        private static bool MatchesKnownPattern(string fileName)
        {
            // === Microsoft Office ===
            // ~$document.docx - Owner/lock files
            if (fileName.StartsWith("~$", StringComparison.Ordinal))
                return true;

            // ~wrf1234.tmp, ~wrd1234.tmp, etc. - Word temp files
            if (fileName.StartsWith("~", StringComparison.Ordinal) &&
                fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                return true;

            // === LibreOffice ===
            // .~lock.document.ods# - Lock files
            if (fileName.StartsWith(".~lock.", StringComparison.Ordinal) &&
                fileName.EndsWith("#", StringComparison.Ordinal))
                return true;

            // === Blender ===
            // document.blend1, document.blend2, etc. - Backup versions
            if (IsBlenderBackup(fileName))
                return true;

            // document.blend@ - Temp during save
            if (fileName.EndsWith(".blend@", StringComparison.OrdinalIgnoreCase))
                return true;

            // === General temp/backup extensions ===
            var ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext))
            {
                var extLower = ext.ToLowerInvariant();
                switch (extLower)
                {
                    // Temp files
                    case ".tmp":
                    case ".temp":
                    // Backup files
                    case ".bak":
                    case ".backup":
                    case ".old":
                    // Editor swap/lock files
                    case ".swp":   // Vim
                    case ".swo":   // Vim
                    case ".swn":   // Vim
                    case ".lock":
                    case ".lck":
                    // AutoRecovery
                    case ".asd":   // Office AutoRecovery
                        return true;
                }
            }

            // Files ending with ~ (common backup suffix)
            // e.g., "document.txt~"
            if (fileName.EndsWith("~", StringComparison.Ordinal) &&
                !fileName.StartsWith("~", StringComparison.Ordinal)) // Not already caught above
                return true;

            // === System files ===
            var fileNameLower = fileName.ToLowerInvariant();
            switch (fileNameLower)
            {
                case "desktop.ini":
                case "thumbs.db":
                case ".ds_store":
                case "icon\r":  // macOS icon file
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if filename matches Blender backup pattern (*.blend1 through *.blend32)
        /// </summary>
        private static bool IsBlenderBackup(string fileName)
        {
            // Check for .blend followed by digits
            var dotIndex = fileName.LastIndexOf(".blend", StringComparison.OrdinalIgnoreCase);
            if (dotIndex < 0)
                return false;

            var afterBlend = fileName.Substring(dotIndex + 6); // After ".blend"
            if (string.IsNullOrEmpty(afterBlend))
                return false;

            // Must be digits only (1-32)
            if (int.TryParse(afterBlend, out int num) && num >= 1 && num <= 32)
                return true;

            return false;
        }

        #endregion

        #region Layer 3: Heuristics

        /// <summary>
        /// Applies heuristics for edge cases not caught by patterns.
        /// </summary>
        private static bool MatchesHeuristics(string fileName, string fullPath)
        {
            // Excel atomic save temp files: 8 hex characters, no extension
            // e.g., "Cedd4100", "AB3F1200"
            if (IsHexTempFile(fileName))
                return true;

            // Hidden files starting with ~ that we haven't caught yet
            // Often these are temp/working files
            if (fileName.StartsWith("~", StringComparison.Ordinal) && IsHiddenFile(fullPath))
                return true;

            // Hidden files starting with . that look like temp files
            // (but not .gitignore, .env, etc. - only if they have suspicious patterns)
            if (fileName.StartsWith(".", StringComparison.Ordinal) &&
                fileName.Length > 1 &&
                IsSuspiciousHiddenFile(fileName))
                return true;

            return false;
        }

        /// <summary>
        /// Checks if filename is an Excel-style hex temp file (8 hex chars, no extension)
        /// </summary>
        private static bool IsHexTempFile(string fileName)
        {
            // Must be exactly 8 characters with no extension
            if (fileName.Length != 8)
                return false;

            // No extension means no dot
            if (fileName.Contains('.'))
                return false;

            return HexTempFileRegex.IsMatch(fileName);
        }

        /// <summary>
        /// Checks if file has the Hidden attribute
        /// </summary>
        private static bool IsHiddenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                var attrs = File.GetAttributes(path);
                return (attrs & FileAttributes.Hidden) != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a hidden file (starting with .) looks suspicious/temporary
        /// </summary>
        private static bool IsSuspiciousHiddenFile(string fileName)
        {
            var lower = fileName.ToLowerInvariant();

            // Suspicious patterns in hidden files
            if (lower.Contains("~lock") ||
                lower.Contains(".tmp") ||
                lower.Contains(".temp") ||
                lower.Contains(".swp"))
                return true;

            // .# files (Emacs backup)
            if (fileName.StartsWith(".#", StringComparison.Ordinal))
                return true;

            return false;
        }

        #endregion
    }
}
