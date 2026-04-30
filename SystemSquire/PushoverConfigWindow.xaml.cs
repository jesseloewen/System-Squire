using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SystemSquire
{
    public partial class PushoverConfigWindow : Window
    {
        private PushoverConfig _workingConfig;
        private bool _suppressAutoSave;

        public event EventHandler<PushoverConfig>? ConfigChanged;

        public PushoverConfigWindow(PushoverConfig? sourceConfig)
        {
            InitializeComponent();

            _workingConfig = CloneConfig(sourceConfig);
            _workingConfig.Normalize();

            LoadFromConfig();
            RefreshRunningApplications();
        }

        public void ApplyExternalConfig(PushoverConfig? sourceConfig)
        {
            _workingConfig = CloneConfig(sourceConfig);
            _workingConfig.Normalize();
            LoadFromConfig();
        }

        private void LoadFromConfig()
        {
            _suppressAutoSave = true;

            EnablePushoverCheckBox.IsChecked = _workingConfig.Enabled;
            ApiTokenBox.Text = _workingConfig.ApiToken;
            UserKeyBox.Text = _workingConfig.UserKey;

            NotifyAppStartCheckBox.IsChecked = _workingConfig.NotifyOnSystemSquireStart;
            NotifyAppCloseCheckBox.IsChecked = _workingConfig.NotifyOnSystemSquireClose;
            NotifyInactivityCheckBox.IsChecked = _workingConfig.NotifyOnInactivity;
            RepeatInactivityCheckBox.IsChecked = _workingConfig.RepeatInactivityNotifications;
            InactivityMinutesBox.Text = Math.Max(1, _workingConfig.InactivityNotificationMinutes).ToString();
            FolderPollingSecondsBox.Text = Math.Max(1, _workingConfig.FolderPollingSeconds).ToString();
            FolderInactivityMinutesBox.Text = Math.Max(1, _workingConfig.FolderInactivityMinutes).ToString();
            RepeatFolderInactivityCheckBox.IsChecked = _workingConfig.RepeatFolderInactivityNotifications;

            LifecycleAppsListBox.Items.Clear();
            foreach (PushoverConfig.PushoverLifecycleAppEntry entry in _workingConfig.LifecycleAppEventEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                LifecycleAppsListBox.Items.Add(new PushoverConfig.PushoverLifecycleAppEntry
                {
                    Name = NormalizeProcessName(entry.Name),
                    NotifyOnStart = entry.NotifyOnStart,
                    NotifyOnClose = entry.NotifyOnClose
                });
            }

            FolderWatchListBox.Items.Clear();
            foreach (PushoverConfig.PushoverFolderWatchEntry entry in _workingConfig.FolderWatchEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.FolderPath))
                {
                    continue;
                }

                FolderWatchListBox.Items.Add(new PushoverConfig.PushoverFolderWatchEntry
                {
                    FolderPath = entry.FolderPath,
                    NotifyOnCreated = entry.NotifyOnCreated,
                    NotifyOnRemoved = entry.NotifyOnRemoved,
                    NotifyOnModified = entry.NotifyOnModified,
                    NotifyOnInactivity = entry.NotifyOnInactivity
                });
            }

            _suppressAutoSave = false;
        }

        private void RefreshWatchedApps_Click(object sender, RoutedEventArgs e)
        {
            RefreshRunningApplications();
        }

        private void AddWatchedApp_Click(object sender, RoutedEventArgs e)
        {
            if (WatchedAppsComboBox.SelectedItem is not string selectedApp)
            {
                return;
            }

            bool alreadyAdded = LifecycleAppsListBox.Items
                .OfType<PushoverConfig.PushoverLifecycleAppEntry>()
                .Any(item => string.Equals(item.Name, selectedApp, StringComparison.OrdinalIgnoreCase));

            if (!alreadyAdded)
            {
                LifecycleAppsListBox.Items.Add(new PushoverConfig.PushoverLifecycleAppEntry
                {
                    Name = NormalizeProcessName(selectedApp),
                    NotifyOnStart = true,
                    NotifyOnClose = true
                });

                AutoSaveIfNeeded();
            }
        }

        private void RemoveWatchedApp_Click(object sender, RoutedEventArgs e)
        {
            if (LifecycleAppsListBox.SelectedItem != null)
            {
                LifecycleAppsListBox.Items.Remove(LifecycleAppsListBox.SelectedItem);
                AutoSaveIfNeeded();
            }
        }

        private void AddFolderWatch_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = FolderPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(folderPath);
            }
            catch
            {
                MessageBox.Show(
                    "The folder path is invalid.",
                    "Pushover Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(normalizedPath))
            {
                MessageBox.Show(
                    "The folder path does not exist.",
                    "Pushover Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool alreadyAdded = FolderWatchListBox.Items
                .OfType<PushoverConfig.PushoverFolderWatchEntry>()
                .Any(item => string.Equals(item.FolderPath, normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (alreadyAdded)
            {
                return;
            }

            FolderWatchListBox.Items.Add(new PushoverConfig.PushoverFolderWatchEntry
            {
                FolderPath = normalizedPath,
                NotifyOnCreated = true,
                NotifyOnRemoved = true,
                NotifyOnModified = true,
                NotifyOnInactivity = false
            });

            FolderPathBox.Text = string.Empty;
            AutoSaveIfNeeded();
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Select folder to watch",
                ShowNewFolderButton = false,
                UseDescriptionForTitle = true
            };

            string currentPath = FolderPathBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            {
                dialog.SelectedPath = currentPath;
            }

            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                FolderPathBox.Text = dialog.SelectedPath;
            }
        }

        private void RemoveFolderWatch_Click(object sender, RoutedEventArgs e)
        {
            if (FolderWatchListBox.SelectedItem != null)
            {
                FolderWatchListBox.Items.Remove(FolderWatchListBox.SelectedItem);
                AutoSaveIfNeeded();
            }
        }

        private void AutoSaveCheckBox_Click(object sender, RoutedEventArgs e)
        {
            AutoSaveIfNeeded();
        }

        private void AutoSaveTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            AutoSaveIfNeeded();
        }

        private void LifecycleEntryChanged_Click(object sender, RoutedEventArgs e)
        {
            AutoSaveIfNeeded();
        }

        private void FolderEntryChanged_Click(object sender, RoutedEventArgs e)
        {
            AutoSaveIfNeeded();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            AutoSaveIfNeeded();
        }

        private void AutoSaveIfNeeded()
        {
            if (_suppressAutoSave)
            {
                return;
            }

            ConfigChanged?.Invoke(this, BuildConfigFromUi());
        }

        private void RefreshRunningApplications()
        {
            string? previousSelection = WatchedAppsComboBox.SelectedItem as string;

            string currentProcessName = Process.GetCurrentProcess().ProcessName;
            var runningApps = new List<string>();

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(process.ProcessName) ||
                        string.Equals(process.ProcessName, currentProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    runningApps.Add(NormalizeProcessName(process.ProcessName));
                }
                catch
                {
                    // Ignore inaccessible process.
                }
                finally
                {
                    process.Dispose();
                }
            }

            runningApps = runningApps
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();

            WatchedAppsComboBox.ItemsSource = runningApps;

            if (!string.IsNullOrWhiteSpace(previousSelection) &&
                runningApps.Contains(previousSelection, StringComparer.OrdinalIgnoreCase))
            {
                WatchedAppsComboBox.SelectedItem = previousSelection;
            }
            else if (runningApps.Count > 0)
            {
                WatchedAppsComboBox.SelectedIndex = 0;
            }
        }

        private PushoverConfig BuildConfigFromUi()
        {
            var lifecycleEntries = LifecycleAppsListBox.Items
                .OfType<PushoverConfig.PushoverLifecycleAppEntry>()
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => new PushoverConfig.PushoverLifecycleAppEntry
                {
                    Name = NormalizeProcessName(entry.Name),
                    NotifyOnStart = entry.NotifyOnStart,
                    NotifyOnClose = entry.NotifyOnClose
                })
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PushoverConfig.PushoverLifecycleAppEntry
                {
                    Name = group.First().Name,
                    NotifyOnStart = group.Any(entry => entry.NotifyOnStart),
                    NotifyOnClose = group.Any(entry => entry.NotifyOnClose)
                })
                .ToList();

            var config = new PushoverConfig
            {
                Enabled = EnablePushoverCheckBox.IsChecked == true,
                ApiToken = ApiTokenBox.Text.Trim(),
                UserKey = UserKeyBox.Text.Trim(),
                NotifyOnSystemSquireStart = NotifyAppStartCheckBox.IsChecked == true,
                NotifyOnSystemSquireClose = NotifyAppCloseCheckBox.IsChecked == true,
                NotifyOnInactivity = NotifyInactivityCheckBox.IsChecked == true,
                RepeatInactivityNotifications = RepeatInactivityCheckBox.IsChecked != false,
                InactivityNotificationMinutes = GetInactivityIntervalMinutes(),
                FolderPollingSeconds = GetFolderPollingSeconds(),
                FolderInactivityMinutes = GetFolderInactivityMinutes(),
                RepeatFolderInactivityNotifications = RepeatFolderInactivityCheckBox.IsChecked != false,
                NotifyOnTrackedAppStart = lifecycleEntries.Any(entry => entry.NotifyOnStart),
                NotifyOnTrackedAppClose = lifecycleEntries.Any(entry => entry.NotifyOnClose),
                LifecycleAppEventEntries = lifecycleEntries,
                FolderWatchEntries = FolderWatchListBox.Items
                    .OfType<PushoverConfig.PushoverFolderWatchEntry>()
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.FolderPath))
                    .Select(entry => new PushoverConfig.PushoverFolderWatchEntry
                    {
                        FolderPath = entry.FolderPath.Trim(),
                        NotifyOnCreated = entry.NotifyOnCreated,
                        NotifyOnRemoved = entry.NotifyOnRemoved,
                        NotifyOnModified = entry.NotifyOnModified,
                        NotifyOnInactivity = entry.NotifyOnInactivity
                    })
                    .GroupBy(entry => entry.FolderPath, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new PushoverConfig.PushoverFolderWatchEntry
                    {
                        FolderPath = group.First().FolderPath,
                        NotifyOnCreated = group.Any(entry => entry.NotifyOnCreated),
                        NotifyOnRemoved = group.Any(entry => entry.NotifyOnRemoved),
                        NotifyOnModified = group.Any(entry => entry.NotifyOnModified),
                        NotifyOnInactivity = group.Any(entry => entry.NotifyOnInactivity)
                    })
                    .ToList()
            };

            config.Normalize();
            return config;
        }

        private static PushoverConfig CloneConfig(PushoverConfig? source)
        {
            if (source == null)
            {
                return new PushoverConfig();
            }

            return new PushoverConfig
            {
                Enabled = source.Enabled,
                ApiToken = source.ApiToken,
                UserKey = source.UserKey,
                NotifyOnSystemSquireStart = source.NotifyOnSystemSquireStart,
                NotifyOnTrackedAppStart = source.NotifyOnTrackedAppStart,
                NotifyOnTrackedAppClose = source.NotifyOnTrackedAppClose,
                NotifyOnSystemSquireClose = source.NotifyOnSystemSquireClose,
                NotifyOnInactivity = source.NotifyOnInactivity,
                RepeatInactivityNotifications = source.RepeatInactivityNotifications,
                InactivityNotificationMinutes = source.InactivityNotificationMinutes,
                FolderPollingSeconds = source.FolderPollingSeconds,
                FolderPollingMinutes = source.FolderPollingMinutes,
                FolderInactivityMinutes = source.FolderInactivityMinutes,
                RepeatFolderInactivityNotifications = source.RepeatFolderInactivityNotifications,
                LifecycleAppsToWatch = source.LifecycleAppsToWatch.ToList(),
                LifecycleAppsToWatchEntries = source.LifecycleAppsToWatchEntries
                    .Select(entry => new ConfiguredAppEntry
                    {
                        Name = entry.Name,
                        Enabled = entry.Enabled
                    })
                    .ToList(),
                LifecycleAppEventEntries = source.LifecycleAppEventEntries
                    .Select(entry => new PushoverConfig.PushoverLifecycleAppEntry
                    {
                        Name = entry.Name,
                        NotifyOnStart = entry.NotifyOnStart,
                        NotifyOnClose = entry.NotifyOnClose
                    })
                    .ToList(),
                FolderWatchEntries = source.FolderWatchEntries
                    .Select(entry => new PushoverConfig.PushoverFolderWatchEntry
                    {
                        FolderPath = entry.FolderPath,
                        NotifyOnCreated = entry.NotifyOnCreated,
                        NotifyOnRemoved = entry.NotifyOnRemoved,
                        NotifyOnModified = entry.NotifyOnModified,
                        NotifyOnInactivity = entry.NotifyOnInactivity
                    })
                    .ToList()
            };
        }

        private int GetInactivityIntervalMinutes()
        {
            if (int.TryParse(InactivityMinutesBox.Text, out int minutes) && minutes > 0)
            {
                return minutes;
            }

            InactivityMinutesBox.Text = "30";
            return 30;
        }

        private int GetFolderPollingSeconds()
        {
            if (int.TryParse(FolderPollingSecondsBox.Text, out int seconds) && seconds > 0)
            {
                return seconds;
            }

            FolderPollingSecondsBox.Text = "60";
            return 60;
        }

        private int GetFolderInactivityMinutes()
        {
            if (int.TryParse(FolderInactivityMinutesBox.Text, out int minutes) && minutes > 0)
            {
                return minutes;
            }

            FolderInactivityMinutesBox.Text = "10";
            return 10;
        }

        private static string NormalizeProcessName(string value)
        {
            string trimmed = value.Trim();
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^4]
                : trimmed;
        }
    }
}
