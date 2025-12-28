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
        public SyncActivityType ActivityType { get; set; }
        public DateTime Timestamp { get; set; }

        public string Icon => ActivityType switch
        {
            SyncActivityType.Uploaded => "\uE898",      // Upload icon
            SyncActivityType.Downloaded => "\uE896",   // Download icon
            SyncActivityType.Deleted => "\uE74D",      // Delete icon
            SyncActivityType.Renamed => "\uE8AC",      // Rename icon
            SyncActivityType.Synced => "\uE8FB",       // Sync icon
            _ => "\uE8B7"                               // File icon
        };

        public string ActivityDescription => ActivityType switch
        {
            SyncActivityType.Uploaded => "Subido a",
            SyncActivityType.Downloaded => "Descargado de",
            SyncActivityType.Deleted => "Eliminado de",
            SyncActivityType.Renamed => "Renombrado en",
            SyncActivityType.Synced => "Sincronizado a",
            _ => "Modificado en"
        };

        public string TimeAgo
        {
            get
            {
                var span = DateTime.Now - Timestamp;
                if (span.TotalMinutes < 1) return "Ahora mismo";
                if (span.TotalMinutes < 60) return $"Hace {(int)span.TotalMinutes} min";
                if (span.TotalHours < 24) return $"Hace {(int)span.TotalHours} hora{((int)span.TotalHours > 1 ? "s" : "")}";
                return $"Hace {(int)span.TotalDays} día{((int)span.TotalDays > 1 ? "s" : "")}";
            }
        }
    }

    public enum SyncActivityType
    {
        Uploaded,
        Downloaded,
        Deleted,
        Renamed,
        Synced
    }
}
