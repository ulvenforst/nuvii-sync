using System;

namespace Nuvii_Sync.Models
{
    /// <summary>
    /// Represents a sync activity item for display in the tray popup.
    /// </summary>
    public class SyncActivityItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public SyncActivityType ActivityType { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Full path to the parent folder (for opening in Explorer).
        /// </summary>
        public string FolderFullPath => System.IO.Path.GetDirectoryName(FullPath) ?? string.Empty;

        public string Icon => ActivityType switch
        {
            SyncActivityType.Uploaded => "\uE898",      // Upload icon
            SyncActivityType.Downloaded => "\uE896",   // Download icon
            SyncActivityType.Deleted => "\uE74D",      // Delete icon
            SyncActivityType.Renamed => "\uE8AC",      // Rename icon
            SyncActivityType.Moved => "\uE8DE",        // Move icon (folder with arrow)
            SyncActivityType.Synced => "\uE8FB",       // Sync icon
            _ => "\uE8B7"                               // File icon
        };

        public string ActivityDescription => ActivityType switch
        {
            SyncActivityType.Uploaded => "Subido a",
            SyncActivityType.Downloaded => "Descargado de",
            SyncActivityType.Deleted => "Eliminado de",
            SyncActivityType.Renamed => "Renombrado en",
            SyncActivityType.Moved => "Movido a",
            SyncActivityType.Synced => "Sincronizado a",
            _ => "Modificado en"
        };

        /// <summary>
        /// Formatted timestamp - shows time for today, date for older.
        /// </summary>
        public string FormattedTime
        {
            get
            {
                var today = DateTime.Today;
                if (Timestamp.Date == today)
                    return Timestamp.ToString("HH:mm");
                if (Timestamp.Date == today.AddDays(-1))
                    return $"Ayer {Timestamp:HH:mm}";
                return Timestamp.ToString("dd/MM HH:mm");
            }
        }

        /// <summary>
        /// Whether this item can be clicked to open the file.
        /// Deleted files cannot be opened.
        /// </summary>
        public bool IsClickable => ActivityType != SyncActivityType.Deleted;

        /// <summary>
        /// Opacity for the text - muted for deleted items.
        /// </summary>
        public double TextOpacity => ActivityType == SyncActivityType.Deleted ? 0.5 : 1.0;
    }

    public enum SyncActivityType
    {
        Uploaded,
        Downloaded,
        Deleted,
        Renamed,
        Moved,
        Synced
    }
}
