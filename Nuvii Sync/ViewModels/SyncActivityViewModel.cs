using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nuvii_Sync.CloudSync;
using Nuvii_Sync.Models;
using Nuvii_Sync.Services;

namespace Nuvii_Sync.ViewModels
{
    /// <summary>
    /// ViewModel for the tray popup showing sync activity.
    /// </summary>
    public partial class SyncActivityViewModel : ObservableObject
    {
        private const int MaxActivityItems = 20;

        [ObservableProperty]
        private string _statusText = "Tus archivos están sincronizados";

        [ObservableProperty]
        private string _statusIcon = "\uE930";

        [ObservableProperty]
        private bool _isSyncing;

        [ObservableProperty]
        private string _syncRootFolder = string.Empty;

        public ObservableCollection<SyncActivityItem> RecentActivity { get; } = new();

        public event EventHandler? OpenSettingsRequested;

        public SyncActivityViewModel()
        {
        }

        [RelayCommand]
        private void OpenFolder()
        {
            // Priority: 1. Active sync root, 2. ViewModel property, 3. Saved settings
            string? folderPath = null;

            if (ProviderFolderLocations.IsInitialized)
            {
                folderPath = ProviderFolderLocations.ClientFolder;
            }
            else if (!string.IsNullOrEmpty(SyncRootFolder))
            {
                folderPath = SyncRootFolder;
            }
            else
            {
                folderPath = SettingsService.ClientFolder;
            }

            if (!string.IsNullOrEmpty(folderPath))
            {
                try
                {
                    Process.Start("explorer.exe", folderPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error opening folder: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void ViewOnline()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://nuvii.cloud",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening browser: {ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenSettings()
        {
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetSyncing(bool isSyncing)
        {
            IsSyncing = isSyncing;
            if (isSyncing)
            {
                StatusText = "Sincronizando...";
                StatusIcon = "\uE895"; // Sync icon
            }
            else
            {
                StatusText = "Tus archivos están sincronizados";
                StatusIcon = "\uE930"; // CheckMark icon
            }
        }

        public void SetError(string errorMessage)
        {
            StatusText = errorMessage;
            StatusIcon = "\uE783"; // Error icon
        }

        public void AddActivity(string fileName, string folderPath, string fullPath, SyncActivityType activityType)
        {
            var item = new SyncActivityItem
            {
                FileName = fileName,
                FolderPath = folderPath,
                FullPath = fullPath,
                ActivityType = activityType,
                Timestamp = DateTime.Now
            };

            // Add to beginning of list
            RecentActivity.Insert(0, item);

            // Keep only the most recent items
            while (RecentActivity.Count > MaxActivityItems)
            {
                RecentActivity.RemoveAt(RecentActivity.Count - 1);
            }
        }

        public void ClearActivity()
        {
            RecentActivity.Clear();
        }

        public void OpenFile(SyncActivityItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.FullPath)) return;

            // Don't try to open deleted files
            if (item.ActivityType == SyncActivityType.Deleted) return;

            try
            {
                if (System.IO.File.Exists(item.FullPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.FullPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening file: {ex.Message}");
            }
        }
    }
}
