using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Hardcodet.Wpf.TaskbarNotification;

namespace SystemSquire
{
    public partial class MainWindow : Window
    {
        private readonly KeyboardHook _keyboardHook;
        private readonly ConfigManager _configManager;
        private readonly SystemOperations _systemOps;
        private readonly PushoverNotificationService _pushoverService;
        private readonly RemoteControlWebService _remoteWebService;
        private PushoverConfigWindow? _pushoverConfigWindow;
        private bool _isRecording = false;
        private string? _recordingTarget = null;
        private TaskbarIcon? _trayIcon;
        private bool _isUpdatingEthernetWolState;
        private bool _isExitingApplication;
        private string _latestSystemStatus = "Monitoring Active";
        private const int DefaultShutdownCountdownSeconds = 10;
        private const int MaxShutdownCountdownSeconds = 600;
        private const int DefaultWebServicePort = 7745;
        private const string EthernetAdapterName = "Ethernet";
        private const string StartupRunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupEntryName = "SystemSquire";

        public MainWindow()
        {
            InitializeComponent();

            _configManager = new ConfigManager();
            _keyboardHook = new KeyboardHook();
            _systemOps = new SystemOperations();
            _pushoverService = new PushoverNotificationService();
            _remoteWebService = new RemoteControlWebService(
                GetRemoteControlStateAsync,
                TriggerShutdownFromRemoteAsync,
                TriggerBlackoutFromRemoteAsync,
                TriggerLockDesktopFromRemoteAsync,
                TriggerPushoverTestFromRemoteAsync,
                SaveRemoteConfigAsync,
                GetRemoteWebAuthSettings,
                VerifyRemoteWebPassword);

            _systemOps.StatusChanged += OnSystemStatusChanged;
            _systemOps.AppLifecycleEventDetected += OnAppLifecycleEventDetected;
            _systemOps.InactivityDetected += OnInactivityDetected;
            _systemOps.FolderWatchEventDetected += OnFolderWatchEventDetected;
            _remoteWebService.ServiceStatusChanged += OnWebServiceStatusChanged;

            // Initialize tray icon from resources
            _trayIcon = (TaskbarIcon)this.FindResource("TrayIcon");
            if (_trayIcon != null)
            {
                _trayIcon.TrayMouseDoubleClick += TrayIcon_TrayMouseDoubleClick;
            }

            LoadConfiguration();
            ApplyWindowPlacementFromConfig();
            RegisterHotkeys();
            _keyboardHook.InstallHook();
            _systemOps.StartLaunchWatchWindow();
            _ = SendStartupNotificationIfEnabledAsync();

            if (_configManager.Config.WebServiceAutoStart || _configManager.Config.AutoOpenWebPageOnStartup)
            {
                TryStartWebService(_configManager.Config.AutoOpenWebPageOnStartup, showFailureDialog: false);
            }

            // Handle minimize to tray
            if (_configManager.Config.StartMinimized)
            {
                this.WindowState = WindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Hide();
            }

            Dispatcher.BeginInvoke(
                new Action(UpdateTrayMenuState),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private MenuItem? TryGetTrayMenuItem(string itemName)
        {
            if (_trayIcon?.ContextMenu == null)
            {
                return null;
            }

            return _trayIcon.ContextMenu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(menuItem =>
                    string.Equals(menuItem.Name, itemName, StringComparison.Ordinal));
        }

        private void UpdateTrayMenuState()
        {
            MenuItem? trayMenuToggleWolItem = TryGetTrayMenuItem("TrayMenuToggleWolItem");
            if (trayMenuToggleWolItem != null)
            {
                trayMenuToggleWolItem.IsEnabled = EthernetWolCheckBox.IsEnabled && !_isUpdatingEthernetWolState;
                trayMenuToggleWolItem.IsChecked = EthernetWolCheckBox.IsChecked == true;
            }

            MenuItem? trayMenuToggleNotificationsItem = TryGetTrayMenuItem("TrayMenuToggleNotificationsItem");
            if (trayMenuToggleNotificationsItem != null)
            {
                trayMenuToggleNotificationsItem.IsChecked = _configManager.Config.Pushover?.Enabled == true;
            }

            MenuItem? trayMenuToggleWebServiceItem = TryGetTrayMenuItem("TrayMenuToggleWebServiceItem");
            if (trayMenuToggleWebServiceItem != null)
            {
                trayMenuToggleWebServiceItem.IsChecked = _remoteWebService.IsRunning;
            }

            MenuItem? trayMenuRestartWebServiceItem = TryGetTrayMenuItem("TrayMenuRestartWebServiceItem");
            if (trayMenuRestartWebServiceItem != null)
            {
                trayMenuRestartWebServiceItem.IsEnabled = _remoteWebService.IsRunning;
            }
        }

        private void ApplyWindowPlacementFromConfig()
        {
            if (HasStoredWindowBounds())
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _configManager.Config.WindowLeft!.Value;
                Top = _configManager.Config.WindowTop!.Value;
                Width = _configManager.Config.WindowWidth!.Value;
                Height = _configManager.Config.WindowHeight!.Value;
            }

            if (_configManager.Config.WindowIsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private bool HasStoredWindowBounds()
        {
            return _configManager.Config.WindowLeft.HasValue &&
                _configManager.Config.WindowTop.HasValue &&
                _configManager.Config.WindowWidth.HasValue &&
                _configManager.Config.WindowHeight.HasValue;
        }

        private void SaveWindowPlacementToConfig()
        {
            Rect bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            _configManager.Config.WindowLeft = bounds.Left;
            _configManager.Config.WindowTop = bounds.Top;
            _configManager.Config.WindowWidth = bounds.Width;
            _configManager.Config.WindowHeight = bounds.Height;
            _configManager.Config.WindowIsMaximized = WindowState == WindowState.Maximized;
        }

        private void LoadConfiguration()
        {
            ShutdownHotkeyBox.Text = _configManager.Config.ShutdownHotkey;
            BlackoutHotkeyBox.Text = _configManager.Config.BlackoutHotkey;
            ShutdownCountdownSecondsBox.Text = _configManager.Config.ShutdownCountdownSeconds.ToString();
            StartAtSystemStartupCheckBox.IsChecked = _configManager.Config.StartAtSystemStartup;
            StartMinimizedCheckBox.IsChecked = _configManager.Config.StartMinimized;

            PopulateConfiguredAppsList(
                AppsToKillListBox,
                _configManager.Config.AppsToKillBeforeShutdownEntries);

            PopulateConfiguredAppsList(
                AppsToWatchAtLaunchListBox,
                _configManager.Config.AppsToWatchAfterLaunchEntries);

            LaunchWatchDurationBox.Text = Math.Max(1, _configManager.Config.LaunchWatchDurationMinutes).ToString();
            LaunchMinimizeDelayBox.Text = Math.Max(0, _configManager.Config.LaunchMinimizeDelaySeconds).ToString();
            WebServicePortBox.Text = NormalizeWebServicePort(_configManager.Config.WebServicePort).ToString();
            WebServiceAutoStartCheckBox.IsChecked = _configManager.Config.WebServiceAutoStart;
            AutoOpenWebPageOnStartupCheckBox.IsChecked = _configManager.Config.AutoOpenWebPageOnStartup;
            WebRequirePasswordCheckBox.IsChecked = _configManager.Config.WebServiceRequirePassword;

            if (_configManager.Config.EthernetWakeOnLanEnabled.HasValue)
            {
                EthernetWolCheckBox.IsChecked = _configManager.Config.EthernetWakeOnLanEnabled.Value;
            }

            UpdateWebPasswordStatusText();

            RefreshRunningApplications();
            _systemOps.SetAppsToKillBeforeShutdown(GetConfiguredAppsToKill());
            _systemOps.SetShutdownCountdownSeconds(GetShutdownCountdownSeconds());
            _systemOps.SetLaunchWatchConfiguration(
                GetConfiguredAppsToWatchAtLaunch(),
                GetLaunchWatchDurationMinutes(),
                GetLaunchMinimizeDelaySeconds());
            ApplyStartupRegistration(_configManager.Config.StartAtSystemStartup, showFailureDialog: false);

            ConfigurePushoverIntegrationFromConfig();

            ApplyTheme();
            RefreshEthernetWakeOnLanState();
            UpdateWebServiceStatusDisplay();
            UpdateTrayMenuState();
        }

        private void ConfigurePushoverIntegrationFromConfig()
        {
            _configManager.Config.Pushover ??= new PushoverConfig();
            _configManager.Config.Pushover.Normalize();

            List<PushoverConfig.PushoverLifecycleAppEntry> lifecycleEntries = _configManager.Config.Pushover
                .LifecycleAppEventEntries
                .Where(entry => entry.NotifyOnStart || entry.NotifyOnClose)
                .ToList();

            bool shouldWatchLifecycleApps = _configManager.Config.Pushover.Enabled && lifecycleEntries.Count > 0;

            List<string> lifecycleApps = shouldWatchLifecycleApps
                ? lifecycleEntries
                    .Select(entry => entry.Name)
                    .ToList()
                : new List<string>();

            _systemOps.SetAppsToWatchForLifecycleNotifications(lifecycleApps);
            _systemOps.StartAppLifecycleWatch();

            bool shouldWatchInactivity = _configManager.Config.Pushover.Enabled &&
                _configManager.Config.Pushover.NotifyOnInactivity;

            _systemOps.ConfigureInactivityWatch(
                shouldWatchInactivity,
                _configManager.Config.Pushover.InactivityNotificationMinutes,
                _configManager.Config.Pushover.RepeatInactivityNotifications);

            List<FolderWatchConfigEntry> folderWatchEntries = _configManager.Config.Pushover
                .FolderWatchEntries
                .Where(entry => entry.NotifyOnCreated || entry.NotifyOnRemoved || entry.NotifyOnModified || entry.NotifyOnInactivity)
                .Select(entry => new FolderWatchConfigEntry
                {
                    FolderPath = entry.FolderPath,
                    NotifyOnCreated = entry.NotifyOnCreated,
                    NotifyOnRemoved = entry.NotifyOnRemoved,
                    NotifyOnModified = entry.NotifyOnModified,
                    NotifyOnInactivity = entry.NotifyOnInactivity
                })
                .ToList();

            bool shouldWatchFolders = _configManager.Config.Pushover.Enabled && folderWatchEntries.Count > 0;

            _systemOps.SetFolderWatchConfiguration(
                shouldWatchFolders ? folderWatchEntries : new List<FolderWatchConfigEntry>(),
                _configManager.Config.Pushover.FolderPollingSeconds,
                _configManager.Config.Pushover.FolderInactivityMinutes,
                _configManager.Config.Pushover.RepeatFolderInactivityNotifications);
            _systemOps.StartFolderWatch();
        }

        private void ApplyTheme()
        {
            this.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            TitleTextBlock.Foreground = new SolidColorBrush(Colors.White);
            SubtitleTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
            FooterTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
            StatusLabelText.Foreground = new SolidColorBrush(Colors.White);
        }

        private void RegisterHotkeys()
        {
            _keyboardHook.ClearHotkeys();

            // Parse and register shutdown hotkey
            var shutdownHotkey = HotkeyDefinition.Parse(_configManager.Config.ShutdownHotkey);
            if (shutdownHotkey.HasValue)
            {
                _keyboardHook.RegisterHotkey(
                    shutdownHotkey.Value.Modifiers,
                    shutdownHotkey.Value.Key,
                    () => _systemOps.TriggerShutdown()
                );
            }

            // Parse and register blackout hotkey
            var blackoutHotkey = HotkeyDefinition.Parse(_configManager.Config.BlackoutHotkey);
            if (blackoutHotkey.HasValue)
            {
                _keyboardHook.RegisterHotkey(
                    blackoutHotkey.Value.Modifiers,
                    blackoutHotkey.Value.Key,
                    () => _systemOps.TriggerBlackout()
                );
            }
        }

        private void OnSystemStatusChanged(object? sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                _latestSystemStatus = status;
                StatusText.Text = status;
                
                if (status.Contains("Error") || status.Contains("Warning"))
                {
                    StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                }
                else if (status.Contains("Cooldown"))
                {
                    StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(243, 156, 18));
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(243, 156, 18));
                }
                else
                {
                    StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                }
            });
        }

        private void RecordShutdownHotkey_Click(object sender, RoutedEventArgs e)
        {
            StartRecording("shutdown");
        }

        private void RecordBlackoutHotkey_Click(object sender, RoutedEventArgs e)
        {
            StartRecording("blackout");
        }

        private void StartRecording(string target)
        {
            if (_isRecording) return;

            _isRecording = true;
            _recordingTarget = target;

            // Temporarily uninstall hook to capture keys
            _keyboardHook.UninstallHook();

            var targetBox = target == "shutdown" ? ShutdownHotkeyBox : BlackoutHotkeyBox;
            targetBox.Text = "Press keys...";
            targetBox.Background = new SolidColorBrush(Color.FromRgb(52, 73, 94));

            // Capture next key combination
            this.PreviewKeyDown += Window_PreviewKeyDown_Recording;
        }

        private void Window_PreviewKeyDown_Recording(object sender, KeyEventArgs e)
        {
            if (!_isRecording) return;

            // Remove handler
            this.PreviewKeyDown -= Window_PreviewKeyDown_Recording;

            // Get modifiers
            ModifierKeys modifiers = ModifierKeys.None;
            if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
                modifiers |= ModifierKeys.Control;
            if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
                modifiers |= ModifierKeys.Alt;
            if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                modifiers |= ModifierKeys.Shift;
            if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows))
                modifiers |= ModifierKeys.Windows;

            // Get main key (not a modifier)
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            
            // Ignore if only modifiers pressed
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                // Restart recording
                _isRecording = false;
                StartRecording(_recordingTarget!);
                return;
            }

            var hotkey = new HotkeyDefinition(modifiers, key);
            var hotkeyString = hotkey.ToString();

            var targetBox = _recordingTarget == "shutdown" ? ShutdownHotkeyBox : BlackoutHotkeyBox;
            targetBox.Text = hotkeyString;
            targetBox.Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));

            _isRecording = false;
            _recordingTarget = null;

            // Reinstall hook
            _keyboardHook.InstallHook();

            SaveMainConfigurationSilently();

            e.Handled = true;
        }

        private void ConfigurePushover_Click(object sender, RoutedEventArgs e)
        {
            if (_pushoverConfigWindow != null)
            {
                _pushoverConfigWindow.ApplyExternalConfig(_configManager.Config.Pushover);
                _pushoverConfigWindow.Activate();
                return;
            }

            _pushoverConfigWindow = new PushoverConfigWindow(_configManager.Config.Pushover)
            {
                Owner = this
            };
            _pushoverConfigWindow.ConfigChanged += PushoverConfigWindow_ConfigChanged;
            _pushoverConfigWindow.Closed += PushoverConfigWindow_Closed;
            _pushoverConfigWindow.ShowDialog();
        }

        private async void TestPushoverNotification_Click(object sender, RoutedEventArgs e)
        {
            (bool success, string message) = await SendTestPushoverNotificationAsync();

            MessageBox.Show(
                message,
                "System Squire",
                MessageBoxButton.OK,
                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void PushoverConfigWindow_Closed(object? sender, EventArgs e)
        {
            if (sender is not PushoverConfigWindow closedWindow)
            {
                return;
            }

            closedWindow.ConfigChanged -= PushoverConfigWindow_ConfigChanged;
            closedWindow.Closed -= PushoverConfigWindow_Closed;

            if (ReferenceEquals(_pushoverConfigWindow, closedWindow))
            {
                _pushoverConfigWindow = null;
            }
        }

        private void PushoverConfigWindow_ConfigChanged(object? sender, PushoverConfig updatedConfig)
        {
            _configManager.Config.Pushover = updatedConfig;
            SaveMainConfigurationSilently();
        }

        private void RefreshRunningApps_Click(object sender, RoutedEventArgs e)
        {
            RefreshRunningApplications();
        }

        private void RefreshLaunchWatchApps_Click(object sender, RoutedEventArgs e)
        {
            RefreshRunningApplications();
        }

        private void AddSelectedApp_Click(object sender, RoutedEventArgs e)
        {
            if (RunningAppsComboBox.SelectedItem is not string selectedApp)
            {
                return;
            }

            bool alreadyAdded = AppsToKillListBox.Items
                .OfType<ConfiguredAppEntry>()
                .Any(item => string.Equals(item.Name, selectedApp, StringComparison.OrdinalIgnoreCase));

            if (!alreadyAdded)
            {
                AppsToKillListBox.Items.Add(new ConfiguredAppEntry
                {
                    Name = NormalizeProcessName(selectedApp),
                    Enabled = true
                });

                SaveMainConfigurationSilently();
            }
        }

        private void RemoveSelectedApp_Click(object sender, RoutedEventArgs e)
        {
            if (AppsToKillListBox.SelectedItem != null)
            {
                AppsToKillListBox.Items.Remove(AppsToKillListBox.SelectedItem);
                SaveMainConfigurationSilently();
            }
        }

        private void AddLaunchWatchApp_Click(object sender, RoutedEventArgs e)
        {
            if (LaunchWatchAppsComboBox.SelectedItem is not string selectedApp)
            {
                return;
            }

            bool alreadyAdded = AppsToWatchAtLaunchListBox.Items
                .OfType<ConfiguredAppEntry>()
                .Any(item => string.Equals(item.Name, selectedApp, StringComparison.OrdinalIgnoreCase));

            if (!alreadyAdded)
            {
                AppsToWatchAtLaunchListBox.Items.Add(new ConfiguredAppEntry
                {
                    Name = NormalizeProcessName(selectedApp),
                    Enabled = true
                });

                SaveMainConfigurationSilently();
            }
        }

        private void RemoveLaunchWatchApp_Click(object sender, RoutedEventArgs e)
        {
            if (AppsToWatchAtLaunchListBox.SelectedItem != null)
            {
                AppsToWatchAtLaunchListBox.Items.Remove(AppsToWatchAtLaunchListBox.SelectedItem);
                SaveMainConfigurationSilently();
            }
        }

        private void RefreshRunningApplications()
        {
            string? previousSelection = RunningAppsComboBox.SelectedItem as string;
            string? previousLaunchWatchSelection = LaunchWatchAppsComboBox.SelectedItem as string;
            List<string> runningApps = GetRunningApplications();

            RunningAppsComboBox.ItemsSource = runningApps;
            LaunchWatchAppsComboBox.ItemsSource = runningApps;

            if (!string.IsNullOrWhiteSpace(previousSelection) &&
                runningApps.Contains(previousSelection, StringComparer.OrdinalIgnoreCase))
            {
                RunningAppsComboBox.SelectedItem = previousSelection;
            }
            else if (runningApps.Count > 0)
            {
                RunningAppsComboBox.SelectedIndex = 0;
            }

            if (!string.IsNullOrWhiteSpace(previousLaunchWatchSelection) &&
                runningApps.Contains(previousLaunchWatchSelection, StringComparer.OrdinalIgnoreCase))
            {
                LaunchWatchAppsComboBox.SelectedItem = previousLaunchWatchSelection;
            }
            else if (runningApps.Count > 0)
            {
                LaunchWatchAppsComboBox.SelectedIndex = 0;
            }
        }

        private List<string> GetRunningApplications()
        {
            string currentProcessName = Process.GetCurrentProcess().ProcessName;
            var runningApps = new List<string>();

            foreach (var process in Process.GetProcesses())
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
                    // Ignore inaccessible processes.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return runningApps
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();
        }

        private List<string> GetConfiguredAppsToKill()
        {
            return GetConfiguredAppEntries(AppsToKillListBox)
                .Where(entry => entry.Enabled)
                .Select(entry => entry.Name)
                .ToList();
        }

        private List<string> GetConfiguredAppsToWatchAtLaunch()
        {
            return GetConfiguredAppEntries(AppsToWatchAtLaunchListBox)
                .Where(entry => entry.Enabled)
                .Select(entry => entry.Name)
                .ToList();
        }

        private List<ConfiguredAppEntry> GetConfiguredAppEntries(ListBox listBox)
        {
            return listBox.Items
                .OfType<ConfiguredAppEntry>()
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => new ConfiguredAppEntry
                {
                    Name = NormalizeProcessName(entry.Name),
                    Enabled = entry.Enabled
                })
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ConfiguredAppEntry
                {
                    Name = group.First().Name,
                    Enabled = group.Any(entry => entry.Enabled)
                })
                .ToList();
        }

        private void PopulateConfiguredAppsList(ListBox listBox, IEnumerable<ConfiguredAppEntry> entries)
        {
            listBox.Items.Clear();

            foreach (ConfiguredAppEntry entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                listBox.Items.Add(new ConfiguredAppEntry
                {
                    Name = NormalizeProcessName(entry.Name),
                    Enabled = entry.Enabled
                });
            }
        }

        private int GetLaunchWatchDurationMinutes()
        {
            if (int.TryParse(LaunchWatchDurationBox.Text, out int minutes) && minutes > 0)
            {
                return minutes;
            }

            LaunchWatchDurationBox.Text = "1";
            return 1;
        }

        private int GetShutdownCountdownSeconds()
        {
            if (int.TryParse(ShutdownCountdownSecondsBox.Text, out int seconds) &&
                seconds >= 0 &&
                seconds <= MaxShutdownCountdownSeconds)
            {
                return seconds;
            }

            ShutdownCountdownSecondsBox.Text = DefaultShutdownCountdownSeconds.ToString();
            return DefaultShutdownCountdownSeconds;
        }

        private int GetLaunchMinimizeDelaySeconds()
        {
            if (int.TryParse(LaunchMinimizeDelayBox.Text, out int seconds) && seconds >= 0)
            {
                return seconds;
            }

            LaunchMinimizeDelayBox.Text = "0";
            return 0;
        }

        private static string NormalizeProcessName(string value)
        {
            string trimmed = value.Trim();
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^4]
                : trimmed;
        }

        private async void RefreshEthernetWakeOnLanState()
        {
            _isUpdatingEthernetWolState = true;
            EthernetWolCheckBox.IsEnabled = false;
            UpdateTrayMenuState();

            bool? wolEnabled = await Task.Run(() => _systemOps.GetEthernetWakeOnLanEnabled(EthernetAdapterName));

            _isUpdatingEthernetWolState = false;

            if (!wolEnabled.HasValue)
            {
                if (TryApplyStoredEthernetWakeOnLanState())
                {
                    UpdateTrayMenuState();
                    return;
                }

                EthernetWolCheckBox.IsChecked = false;
                EthernetWolCheckBox.IsEnabled = false;
                UpdateTrayMenuState();
                return;
            }

            EthernetWolCheckBox.IsChecked = wolEnabled.Value;
            EthernetWolCheckBox.IsEnabled = true;
            SaveEthernetWakeOnLanState(wolEnabled.Value);
            UpdateTrayMenuState();
        }

        private async void EthernetWolCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingEthernetWolState)
            {
                return;
            }

            bool targetState = EthernetWolCheckBox.IsChecked == true;
            await SetEthernetWakeOnLanAsync(targetState);
        }

        private async Task SetEthernetWakeOnLanAsync(bool targetState)
        {
            bool fallbackState = _configManager.Config.EthernetWakeOnLanEnabled ?? !targetState;

            _isUpdatingEthernetWolState = true;
            EthernetWolCheckBox.IsChecked = targetState;
            EthernetWolCheckBox.IsEnabled = false;
            _isUpdatingEthernetWolState = false;
            UpdateTrayMenuState();

            ElevatedOperationResult result = await Task.Run(() => _systemOps.SetEthernetWakeOnLanEnabled(targetState, EthernetAdapterName));
            if (result == ElevatedOperationResult.Cancelled)
            {
                _isUpdatingEthernetWolState = true;
                EthernetWolCheckBox.IsChecked = fallbackState;
                _isUpdatingEthernetWolState = false;
                EthernetWolCheckBox.IsEnabled = true;
                UpdateTrayMenuState();

                MessageBox.Show(
                    "Wake-on-LAN change was canceled because administrator approval was not granted.",
                    "System Squire",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (result == ElevatedOperationResult.Failed)
            {
                RefreshEthernetWakeOnLanState();
                return;
            }

            SaveEthernetWakeOnLanState(targetState);
            RefreshEthernetWakeOnLanState();
            SaveMainConfigurationSilently();
        }

        private void MainSettingChanged_Click(object sender, RoutedEventArgs e)
        {
            SaveMainConfigurationSilently();
        }

        private void WebRequirePasswordCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool requiresPassword = WebRequirePasswordCheckBox.IsChecked == true;
            bool hasPassword = !string.IsNullOrWhiteSpace(_configManager.Config.WebServicePasswordHash) &&
                !string.IsNullOrWhiteSpace(_configManager.Config.WebServicePasswordSalt);

            if (requiresPassword && !hasPassword)
            {
                WebRequirePasswordCheckBox.IsChecked = false;
                MessageBox.Show(
                    "Set a web remote password before enabling required login.",
                    "System Squire",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            SaveMainConfigurationSilently();
            UpdateWebPasswordStatusText();
        }

        private void SetWebPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            string password = WebPasswordBox.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 8)
            {
                MessageBox.Show(
                    "Please provide a password with at least 8 characters.",
                    "System Squire",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            (string hash, string salt) = WebPasswordSecurity.CreateHash(password.Trim());
            _configManager.Config.WebServicePasswordHash = hash;
            _configManager.Config.WebServicePasswordSalt = salt;

            if (WebRequirePasswordCheckBox.IsChecked == true)
            {
                _configManager.Config.WebServiceRequirePassword = true;
            }

            WebPasswordBox.Password = string.Empty;
            SaveMainConfigurationSilently();
            UpdateWebPasswordStatusText("Web remote password updated.");
        }

        private void MainSettingChanged_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveMainConfigurationSilently();
        }

        private void AppsListEntryCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SaveMainConfigurationSilently();
        }

        private void SaveMainConfigurationSilently()
        {
            PersistConfiguration(showConfirmation: false);
        }

        private bool TryApplyStoredEthernetWakeOnLanState()
        {
            if (!_configManager.Config.EthernetWakeOnLanEnabled.HasValue)
            {
                return false;
            }

            bool storedState = _configManager.Config.EthernetWakeOnLanEnabled.Value;
            _isUpdatingEthernetWolState = true;
            EthernetWolCheckBox.IsChecked = storedState;
            _isUpdatingEthernetWolState = false;
            EthernetWolCheckBox.IsEnabled = true;
            UpdateTrayMenuState();
            return true;
        }

        private void SaveEthernetWakeOnLanState(bool enabled)
        {
            if (_configManager.Config.EthernetWakeOnLanEnabled == enabled)
            {
                return;
            }

            _configManager.Config.EthernetWakeOnLanEnabled = enabled;
            _configManager.SaveConfig();
        }

        private void RunShutdownButton_Click(object sender, RoutedEventArgs e)
        {
            _systemOps.TriggerShutdown();
        }

        private void ToggleBlackoutButton_Click(object sender, RoutedEventArgs e)
        {
            _systemOps.TriggerBlackout();
        }

        private void StartWebService_Click(object sender, RoutedEventArgs e)
        {
            TryStartWebService(openWebPage: false, showFailureDialog: true);
        }

        private void StopWebService_Click(object sender, RoutedEventArgs e)
        {
            _remoteWebService.Stop();
            UpdateWebServiceStatusDisplay();
        }

        private void RestartWebService_Click(object sender, RoutedEventArgs e)
        {
            RestartWebService(showFailureDialog: true);
        }

        private void OpenWebPage_Click(object sender, RoutedEventArgs e)
        {
            if (!_remoteWebService.IsRunning)
            {
                MessageBox.Show(
                    "Web service is not running. Start the service first.",
                    "System Squire",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            OpenWebControlPage();
        }

        private void OnWebServiceStatusChanged(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateWebServiceStatusDisplay(message);
            });
        }

        private void UpdateWebServiceStatusDisplay(string? overrideMessage = null)
        {
            bool running = _remoteWebService.IsRunning;
            string message = overrideMessage ?? (running
                ? $"Web service running at {_remoteWebService.BaseUrl}"
                : "Web service is stopped.");

            WebServiceStatusText.Text = message;
            WebServiceStatusText.Foreground = running
                ? new SolidColorBrush(Color.FromRgb(46, 204, 113))
                : new SolidColorBrush(Color.FromRgb(184, 184, 184));

            StartWebServiceButton.IsEnabled = !running;
            StopWebServiceButton.IsEnabled = running;
            RestartWebServiceButton.IsEnabled = running;
            UpdateTrayMenuState();
        }

        private void RestartWebService(bool showFailureDialog)
        {
            if (_remoteWebService.IsRunning)
            {
                _remoteWebService.Stop();
            }

            TryStartWebService(openWebPage: false, showFailureDialog: showFailureDialog);
            UpdateTrayMenuState();
        }

        private bool TryStartWebService(bool openWebPage, bool showFailureDialog)
        {
            int port = GetWebServicePort();

            if (_remoteWebService.IsRunning && _remoteWebService.Port == port)
            {
                UpdateWebServiceStatusDisplay($"Web service already running at {_remoteWebService.BaseUrl}");
                if (openWebPage)
                {
                    OpenWebControlPage();
                }

                return true;
            }

            if (_remoteWebService.IsRunning)
            {
                _remoteWebService.Stop();
            }

            if (!_remoteWebService.Start(port, out string message))
            {
                UpdateWebServiceStatusDisplay(message);
                WebServiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));

                if (showFailureDialog)
                {
                    MessageBox.Show(
                        message,
                        "System Squire",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return false;
            }

            UpdateWebServiceStatusDisplay(message);

            if (openWebPage)
            {
                OpenWebControlPage();
            }

            return true;
        }

        private void OpenWebControlPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _remoteWebService.LocalBaseUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UpdateWebServiceStatusDisplay($"Unable to open browser: {ex.Message}");
                WebServiceStatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
            }
        }

        private int GetWebServicePort()
        {
            if (int.TryParse(WebServicePortBox.Text, out int port))
            {
                int normalized = NormalizeWebServicePort(port);
                WebServicePortBox.Text = normalized.ToString();
                return normalized;
            }

            WebServicePortBox.Text = DefaultWebServicePort.ToString();
            return DefaultWebServicePort;
        }

        private static int NormalizeWebServicePort(int port)
        {
            return port is >= 1 and <= 65535 ? port : DefaultWebServicePort;
        }

        private Task InvokeOnUiThreadAsync(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return Dispatcher.InvokeAsync(action).Task;
        }

        private Task<T> InvokeOnUiThreadAsync<T>(Func<T> action)
        {
            if (Dispatcher.CheckAccess())
            {
                return Task.FromResult(action());
            }

            return Dispatcher.InvokeAsync(action).Task;
        }

        private async Task<RemoteControlState> GetRemoteControlStateAsync()
        {
            return await InvokeOnUiThreadAsync(() => new RemoteControlState
            {
                StatusText = _latestSystemStatus,
                ShutdownCountdownSeconds = GetShutdownCountdownSeconds(),
                LaunchWatchDurationMinutes = GetLaunchWatchDurationMinutes(),
                LaunchMinimizeDelaySeconds = GetLaunchMinimizeDelaySeconds(),
                AppsToKillBeforeShutdown = GetConfiguredAppEntries(AppsToKillListBox),
                AppsToWatchAfterLaunch = GetConfiguredAppEntries(AppsToWatchAtLaunchListBox),
                RunningApplications = GetRunningApplications(),
                Pushover = ClonePushoverConfigForRemote(_configManager.Config.Pushover),
                WebServiceRunning = _remoteWebService.IsRunning,
                WebServicePort = _remoteWebService.Port > 0
                    ? _remoteWebService.Port
                    : GetWebServicePort()
            });
        }

        private RemoteWebAuthSettings GetRemoteWebAuthSettings()
        {
            return new RemoteWebAuthSettings
            {
                RequirePassword = _configManager.Config.WebServiceRequirePassword,
                PasswordHash = _configManager.Config.WebServicePasswordHash,
                PasswordSalt = _configManager.Config.WebServicePasswordSalt
            };
        }

        private bool VerifyRemoteWebPassword(string password)
        {
            return WebPasswordSecurity.Verify(
                password,
                _configManager.Config.WebServicePasswordHash,
                _configManager.Config.WebServicePasswordSalt);
        }

        private async Task<RemoteOperationResponse> TriggerShutdownFromRemoteAsync()
        {
            await InvokeOnUiThreadAsync(() => _systemOps.TriggerShutdown());

            return new RemoteOperationResponse
            {
                Success = true,
                Message = "Shutdown action toggled.",
                State = await GetRemoteControlStateAsync()
            };
        }

        private async Task<RemoteOperationResponse> TriggerBlackoutFromRemoteAsync()
        {
            await InvokeOnUiThreadAsync(() => _systemOps.TriggerBlackout());

            return new RemoteOperationResponse
            {
                Success = true,
                Message = "Blackout triggered.",
                State = await GetRemoteControlStateAsync()
            };
        }

        private async Task<RemoteOperationResponse> TriggerLockDesktopFromRemoteAsync()
        {
            bool locked = false;
            await InvokeOnUiThreadAsync(() =>
            {
                locked = _systemOps.TriggerDesktopLock();
            });

            return new RemoteOperationResponse
            {
                Success = locked,
                Message = locked ? "Desktop locked." : "Failed to lock desktop.",
                State = await GetRemoteControlStateAsync()
            };
        }

        private async Task<RemoteOperationResponse> TriggerPushoverTestFromRemoteAsync()
        {
            (bool success, string message) = await SendTestPushoverNotificationAsync();

            return new RemoteOperationResponse
            {
                Success = success,
                Message = message,
                State = await GetRemoteControlStateAsync()
            };
        }

        private async Task<RemoteOperationResponse> SaveRemoteConfigAsync(RemoteConfigUpdateRequest request)
        {
            await InvokeOnUiThreadAsync(() =>
            {
                ApplyRemoteConfigToUi(request);
                ApplyRemotePushoverConfig(request.Pushover);
                SyncOpenPushoverConfigWindow();
                PersistConfiguration(showConfirmation: false);
            });

            return new RemoteOperationResponse
            {
                Success = true,
                Message = "Configuration saved.",
                State = await GetRemoteControlStateAsync()
            };
        }

        private void ApplyRemoteConfigToUi(RemoteConfigUpdateRequest request)
        {
            ShutdownCountdownSecondsBox.Text = Math.Clamp(
                request.ShutdownCountdownSeconds,
                0,
                MaxShutdownCountdownSeconds).ToString();
            LaunchWatchDurationBox.Text = Math.Max(1, request.LaunchWatchDurationMinutes).ToString();
            LaunchMinimizeDelayBox.Text = Math.Max(0, request.LaunchMinimizeDelaySeconds).ToString();

            PopulateConfiguredAppsList(
                AppsToKillListBox,
                NormalizeConfiguredEntriesForRemote(request.AppsToKillBeforeShutdown));

            PopulateConfiguredAppsList(
                AppsToWatchAtLaunchListBox,
                NormalizeConfiguredEntriesForRemote(request.AppsToWatchAfterLaunch));
        }

        private void ApplyRemotePushoverConfig(PushoverConfig? requestPushover)
        {
            if (requestPushover == null)
            {
                return;
            }

            string existingApiToken = _configManager.Config.Pushover?.ApiToken ?? string.Empty;
            string existingUserKey = _configManager.Config.Pushover?.UserKey ?? string.Empty;

            PushoverConfig updatedConfig = ClonePushoverConfig(requestPushover);
            updatedConfig.ApiToken = existingApiToken;
            updatedConfig.UserKey = existingUserKey;
            updatedConfig.Normalize();

            _configManager.Config.Pushover = updatedConfig;
        }

        private void SyncOpenPushoverConfigWindow()
        {
            if (_pushoverConfigWindow == null)
            {
                return;
            }

            _pushoverConfigWindow.ApplyExternalConfig(_configManager.Config.Pushover);
        }

        private static PushoverConfig ClonePushoverConfig(PushoverConfig? source)
        {
            if (source == null)
            {
                return new PushoverConfig();
            }

            var clone = new PushoverConfig
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

            clone.Normalize();
            return clone;
        }

        private static PushoverConfig ClonePushoverConfigForRemote(PushoverConfig? source)
        {
            PushoverConfig clone = ClonePushoverConfig(source);
            clone.ApiToken = string.Empty;
            clone.UserKey = string.Empty;
            return clone;
        }

        private static List<ConfiguredAppEntry> NormalizeConfiguredEntriesForRemote(IEnumerable<ConfiguredAppEntry>? entries)
        {
            return entries?
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => new ConfiguredAppEntry
                {
                    Name = NormalizeProcessName(entry.Name),
                    Enabled = entry.Enabled
                })
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ConfiguredAppEntry
                {
                    Name = group.First().Name,
                    Enabled = group.Any(item => item.Enabled)
                })
                .ToList() ?? new List<ConfiguredAppEntry>();
        }

        private void PersistConfiguration(bool showConfirmation)
        {
            int previousWebServicePort = _configManager.Config.WebServicePort;
            bool webServiceWasRunning = _remoteWebService.IsRunning;
            bool previousStartAtSystemStartup = _configManager.Config.StartAtSystemStartup;

            SaveWindowPlacementToConfig();

            _configManager.Config.ShutdownHotkey = ShutdownHotkeyBox.Text;
            _configManager.Config.BlackoutHotkey = BlackoutHotkeyBox.Text;
            _configManager.Config.StartAtSystemStartup = StartAtSystemStartupCheckBox.IsChecked == true;
            _configManager.Config.StartMinimized = StartMinimizedCheckBox.IsChecked ?? false;
            _configManager.Config.WebServicePort = GetWebServicePort();
            _configManager.Config.WebServiceAutoStart = WebServiceAutoStartCheckBox.IsChecked == true;
            _configManager.Config.AutoOpenWebPageOnStartup = AutoOpenWebPageOnStartupCheckBox.IsChecked == true;
            _configManager.Config.WebServiceRequirePassword = WebRequirePasswordCheckBox.IsChecked == true;

            _configManager.Config.AppsToKillBeforeShutdownEntries = GetConfiguredAppEntries(AppsToKillListBox);
            _configManager.Config.AppsToWatchAfterLaunchEntries = GetConfiguredAppEntries(AppsToWatchAtLaunchListBox);

            _configManager.Config.AppsToKillBeforeShutdown = GetConfiguredAppsToKill();
            _configManager.Config.AppsToWatchAfterLaunch = GetConfiguredAppsToWatchAtLaunch();
            _configManager.Config.ShutdownCountdownSeconds = GetShutdownCountdownSeconds();
            _configManager.Config.LaunchWatchDurationMinutes = GetLaunchWatchDurationMinutes();
            _configManager.Config.LaunchMinimizeDelaySeconds = GetLaunchMinimizeDelaySeconds();
            _configManager.Config.EthernetWakeOnLanEnabled = EthernetWolCheckBox.IsChecked;
            _configManager.Config.Pushover ??= new PushoverConfig();
            _configManager.Config.Pushover.Normalize();

            _configManager.SaveConfig();
            if (previousStartAtSystemStartup != _configManager.Config.StartAtSystemStartup)
            {
                ApplyStartupRegistration(_configManager.Config.StartAtSystemStartup, showFailureDialog: true);
            }

            UpdateWebPasswordStatusText();
            _systemOps.SetAppsToKillBeforeShutdown(_configManager.Config.AppsToKillBeforeShutdown);
            _systemOps.SetShutdownCountdownSeconds(_configManager.Config.ShutdownCountdownSeconds);
            _systemOps.SetLaunchWatchConfiguration(
                _configManager.Config.AppsToWatchAfterLaunch,
                _configManager.Config.LaunchWatchDurationMinutes,
                _configManager.Config.LaunchMinimizeDelaySeconds);
            ConfigurePushoverIntegrationFromConfig();
            RegisterHotkeys();

            if (webServiceWasRunning && previousWebServicePort != _configManager.Config.WebServicePort)
            {
                TryStartWebService(openWebPage: false, showFailureDialog: false);
            }
            else
            {
                UpdateWebServiceStatusDisplay();
            }

            if (showConfirmation)
            {
                MessageBox.Show("Configuration saved successfully!", "System Squire",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateWebPasswordStatusText(string? overrideMessage = null)
        {
            bool hasPassword = !string.IsNullOrWhiteSpace(_configManager.Config.WebServicePasswordHash) &&
                !string.IsNullOrWhiteSpace(_configManager.Config.WebServicePasswordSalt);
            bool requiresPassword = WebRequirePasswordCheckBox.IsChecked == true;

            string message = overrideMessage ?? (hasPassword
                ? (requiresPassword
                    ? "Web login password is set and required for remote access."
                    : "Web login password is set but optional.")
                : "Web password is not set.");

            WebPasswordStatusText.Text = message;
            WebPasswordStatusText.Foreground = hasPassword
                ? new SolidColorBrush(Color.FromRgb(46, 204, 113))
                : new SolidColorBrush(Color.FromRgb(243, 156, 18));
        }

        private void ApplyStartupRegistration(bool enabled, bool showFailureDialog)
        {
            try
            {
                if (enabled)
                {
                    string executablePath = GetCurrentExecutablePath();
                    string startupCommand = $"\"{executablePath}\"";

                    using RegistryKey? runKey = Registry.CurrentUser.CreateSubKey(StartupRunRegistryPath, writable: true);
                    if (runKey == null)
                    {
                        throw new InvalidOperationException("Unable to open startup registry key.");
                    }

                    string? existingCommand = runKey.GetValue(StartupEntryName) as string;
                    if (!string.Equals(existingCommand, startupCommand, StringComparison.OrdinalIgnoreCase))
                    {
                        runKey.SetValue(StartupEntryName, startupCommand, RegistryValueKind.String);
                    }

                    return;
                }

                using RegistryKey? existingRunKey = Registry.CurrentUser.OpenSubKey(StartupRunRegistryPath, writable: true);
                existingRunKey?.DeleteValue(StartupEntryName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                if (showFailureDialog)
                {
                    MessageBox.Show(
                        $"Unable to update startup setting: {ex.Message}",
                        "System Squire",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private static string GetCurrentExecutablePath()
        {
            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Unable to resolve current executable path.");
            }

            return executablePath;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExitingApplication)
            {
                return;
            }

            PersistConfiguration(showConfirmation: false);
            e.Cancel = true;
            this.Hide();
            this.ShowInTaskbar = false;
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void TrayContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            UpdateTrayMenuState();
        }

        private void MenuItem_Shutdown_Click(object sender, RoutedEventArgs e)
        {
            _systemOps.TriggerShutdown();
        }

        private void MenuItem_Blackout_Click(object sender, RoutedEventArgs e)
        {
            _systemOps.TriggerBlackout();
        }

        private async void MenuItem_ToggleWol_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingEthernetWolState)
            {
                return;
            }

            if (sender is not MenuItem menuItem)
            {
                return;
            }

            await SetEthernetWakeOnLanAsync(menuItem.IsChecked);
        }

        private void MenuItem_ToggleNotifications_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            _configManager.Config.Pushover ??= new PushoverConfig();
            bool enableNotifications = menuItem.IsChecked;

            if (enableNotifications &&
                (string.IsNullOrWhiteSpace(_configManager.Config.Pushover.ApiToken) ||
                 string.IsNullOrWhiteSpace(_configManager.Config.Pushover.UserKey)))
            {
                menuItem.IsChecked = false;
                enableNotifications = false;

                MessageBox.Show(
                    "Set your Pushover App Token and User Key before enabling notifications.",
                    "System Squire",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            _configManager.Config.Pushover.Enabled = enableNotifications;
            _configManager.Config.Pushover.Normalize();
            _configManager.SaveConfig();
            ConfigurePushoverIntegrationFromConfig();
            UpdateTrayMenuState();
        }

        private void MenuItem_ToggleWebService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            if (menuItem.IsChecked)
            {
                if (!TryStartWebService(openWebPage: false, showFailureDialog: true))
                {
                    menuItem.IsChecked = false;
                }
            }
            else
            {
                _remoteWebService.Stop();
                UpdateWebServiceStatusDisplay();
            }

            UpdateTrayMenuState();
        }

        private void MenuItem_RestartWebService_Click(object sender, RoutedEventArgs e)
        {
            RestartWebService(showFailureDialog: true);
        }

        private void MenuItem_Show_Click(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
        }

        private async void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            await ExitApplicationAsync(sendCloseNotification: true);
        }

        internal Task ExitForInstanceReplacementAsync()
        {
            return ExitApplicationAsync(sendCloseNotification: false);
        }

        private async Task ExitApplicationAsync(bool sendCloseNotification)
        {
            if (_isExitingApplication)
            {
                return;
            }

            PersistConfiguration(showConfirmation: false);
            _isExitingApplication = true;

            if (sendCloseNotification)
            {
                await SendCloseNotificationIfEnabledAsync();
            }

            _systemOps.StopLaunchWatchWindow();
            _systemOps.StopAppLifecycleWatch();
            _systemOps.StopInactivityWatch();
            _systemOps.StopFolderWatch();
            _remoteWebService.Stop();
            _keyboardHook.Dispose();
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            _systemOps.StopLaunchWatchWindow();
            _systemOps.StopAppLifecycleWatch();
            _systemOps.StopInactivityWatch();
            _systemOps.StopFolderWatch();
            _remoteWebService.Stop();
            _keyboardHook.Dispose();
            _trayIcon?.Dispose();
            base.OnClosed(e);
        }

        private void OnAppLifecycleEventDetected(object? sender, AppLifecycleEventArgs e)
        {
            PushoverConfig? pushover = _configManager.Config.Pushover;
            if (pushover == null || !pushover.Enabled)
            {
                return;
            }

            PushoverConfig.PushoverLifecycleAppEntry? appEntry = pushover.LifecycleAppEventEntries
                .FirstOrDefault(entry => string.Equals(entry.Name, e.AppName, StringComparison.OrdinalIgnoreCase));

            if (appEntry == null)
            {
                return;
            }

            if (e.EventType == AppLifecycleEventType.Started && !appEntry.NotifyOnStart)
            {
                return;
            }

            if (e.EventType == AppLifecycleEventType.Closed && !appEntry.NotifyOnClose)
            {
                return;
            }

            string eventText = e.EventType == AppLifecycleEventType.Started ? "started" : "closed";
            _ = SendPushoverNotificationAsync("System Squire", $"Monitored app {e.AppName} {eventText}.");
        }

        private void OnInactivityDetected(object? sender, InactivityEventArgs e)
        {
            PushoverConfig? pushover = _configManager.Config.Pushover;
            if (pushover == null || !pushover.Enabled || !pushover.NotifyOnInactivity)
            {
                return;
            }

            int idleMinutes = Math.Max(1, (int)Math.Floor(e.IdleDuration.TotalMinutes));

            _ = SendPushoverNotificationAsync(
                "System Squire",
                $"No user input detected for {idleMinutes} minute(s)");
        }

        private void OnFolderWatchEventDetected(object? sender, FolderWatchEventArgs e)
        {
            PushoverConfig? pushover = _configManager.Config.Pushover;
            if (pushover == null || !pushover.Enabled)
            {
                return;
            }

            string folderName = e.FolderPath;
            string message = e.EventType switch
            {
                FolderWatchEventType.Created => $"File created in {folderName}: {e.RelativePath}",
                FolderWatchEventType.Removed => $"File removed from {folderName}: {e.RelativePath}",
                FolderWatchEventType.Modified => $"File modified in {folderName}: {e.RelativePath}",
                FolderWatchEventType.Inactive =>
                    $"No file changes detected in {folderName} for {Math.Max(1, (int)Math.Floor(e.InactivityDuration.TotalMinutes))} minute(s).",
                _ => $"Folder event detected in {folderName}."
            };

            _ = SendPushoverNotificationAsync("System Squire", message);
        }

        private async Task SendStartupNotificationIfEnabledAsync()
        {
            PushoverConfig? pushover = _configManager.Config.Pushover;
            if (pushover == null || !pushover.NotifyOnSystemSquireStart)
            {
                return;
            }

            await SendPushoverNotificationAsync("System Squire", "System Squire started.");
        }

        private async Task SendCloseNotificationIfEnabledAsync()
        {
            PushoverConfig? pushover = _configManager.Config.Pushover;
            if (pushover == null || !pushover.NotifyOnSystemSquireClose)
            {
                return;
            }

            await SendPushoverNotificationAsync("System Squire", "System Squire is closing.");
        }

        private async Task<(bool Success, string Message)> SendTestPushoverNotificationAsync()
        {
            PushoverConfig pushover = await InvokeOnUiThreadAsync(() =>
                ClonePushoverConfig(_configManager.Config.Pushover));

            if (!pushover.Enabled)
            {
                return (false, "Pushover notifications are disabled. Enable them in Pushover settings.");
            }

            if (string.IsNullOrWhiteSpace(pushover.ApiToken) || string.IsNullOrWhiteSpace(pushover.UserKey))
            {
                return (false, "Pushover App Token and User Key must be configured from the desktop UI.");
            }

            bool sent = await _pushoverService.SendAsync(
                pushover,
                "System Squire",
                "Test notification from System Squire.");

            return sent
                ? (true, "Test notification sent.")
                : (false, "Failed to send test notification. Verify your credentials and internet connection.");
        }

        private async Task<bool> SendPushoverNotificationAsync(string title, string message)
        {
            PushoverConfig? pushover = _configManager.Config.Pushover;
            if (!_pushoverService.IsReady(pushover))
            {
                return false;
            }

            return await _pushoverService.SendAsync(pushover, title, message);
        }
    }
}
