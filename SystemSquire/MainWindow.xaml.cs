using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;

namespace SystemSquire
{
    public partial class MainWindow : Window
    {
        private readonly KeyboardHook _keyboardHook;
        private readonly ConfigManager _configManager;
        private readonly SystemOperations _systemOps;
        private bool _isRecording = false;
        private string? _recordingTarget = null;
        private TaskbarIcon? _trayIcon;
        private bool _isUpdatingEthernetWolState;
        private const string EthernetAdapterName = "Ethernet";

        public MainWindow()
        {
            InitializeComponent();

            _configManager = new ConfigManager();
            _keyboardHook = new KeyboardHook();
            _systemOps = new SystemOperations();

            _systemOps.StatusChanged += OnSystemStatusChanged;

            // Initialize tray icon from resources
            _trayIcon = (TaskbarIcon)this.FindResource("TrayIcon");
            if (_trayIcon != null)
            {
                _trayIcon.TrayMouseDoubleClick += TrayIcon_TrayMouseDoubleClick;
            }

            LoadConfiguration();
            RegisterHotkeys();
            _keyboardHook.InstallHook();
            _systemOps.StartLaunchWatchWindow();

            // Handle minimize to tray
            if (_configManager.Config.StartMinimized)
            {
                this.WindowState = WindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Hide();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            MinWidth = Math.Ceiling(ActualWidth);
            MinHeight = Math.Ceiling(ActualHeight);
        }

        private void LoadConfiguration()
        {
            ShutdownHotkeyBox.Text = _configManager.Config.ShutdownHotkey;
            BlackoutHotkeyBox.Text = _configManager.Config.BlackoutHotkey;
            DarkModeCheckBox.IsChecked = _configManager.Config.DarkMode;
            StartMinimizedCheckBox.IsChecked = _configManager.Config.StartMinimized;

            AppsToKillListBox.Items.Clear();
            foreach (string appName in _configManager.Config.AppsToKillBeforeShutdown)
            {
                AppsToKillListBox.Items.Add(NormalizeProcessName(appName));
            }

            AppsToWatchAtLaunchListBox.Items.Clear();
            foreach (string appName in _configManager.Config.AppsToWatchAfterLaunch)
            {
                AppsToWatchAtLaunchListBox.Items.Add(NormalizeProcessName(appName));
            }

            LaunchWatchDurationBox.Text = Math.Max(1, _configManager.Config.LaunchWatchDurationMinutes).ToString();
            LaunchMinimizeDelayBox.Text = Math.Max(0, _configManager.Config.LaunchMinimizeDelaySeconds).ToString();

            RefreshRunningApplications();
            _systemOps.SetAppsToKillBeforeShutdown(GetConfiguredAppsToKill());
            _systemOps.SetLaunchWatchConfiguration(
                GetConfiguredAppsToWatchAtLaunch(),
                GetLaunchWatchDurationMinutes(),
                GetLaunchMinimizeDelaySeconds());

            ApplyTheme();
            RefreshEthernetWakeOnLanState();
        }

        private void ApplyTheme()
        {
            if (_configManager.Config.DarkMode)
            {
                this.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                TitleTextBlock.Foreground = new SolidColorBrush(Colors.White);
                SubtitleTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
                FooterTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
                StatusLabelText.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                this.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                TitleTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                SubtitleTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(70, 70, 70));
                FooterTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90));
                StatusLabelText.Foreground = new SolidColorBrush(Colors.White);
            }
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

            e.Handled = true;
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            PersistConfiguration(showConfirmation: true);
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
                .OfType<string>()
                .Any(item => string.Equals(item, selectedApp, StringComparison.OrdinalIgnoreCase));

            if (!alreadyAdded)
            {
                AppsToKillListBox.Items.Add(selectedApp);
            }
        }

        private void RemoveSelectedApp_Click(object sender, RoutedEventArgs e)
        {
            if (AppsToKillListBox.SelectedItem != null)
            {
                AppsToKillListBox.Items.Remove(AppsToKillListBox.SelectedItem);
            }
        }

        private void AddLaunchWatchApp_Click(object sender, RoutedEventArgs e)
        {
            if (LaunchWatchAppsComboBox.SelectedItem is not string selectedApp)
            {
                return;
            }

            bool alreadyAdded = AppsToWatchAtLaunchListBox.Items
                .OfType<string>()
                .Any(item => string.Equals(item, selectedApp, StringComparison.OrdinalIgnoreCase));

            if (!alreadyAdded)
            {
                AppsToWatchAtLaunchListBox.Items.Add(selectedApp);
            }
        }

        private void RemoveLaunchWatchApp_Click(object sender, RoutedEventArgs e)
        {
            if (AppsToWatchAtLaunchListBox.SelectedItem != null)
            {
                AppsToWatchAtLaunchListBox.Items.Remove(AppsToWatchAtLaunchListBox.SelectedItem);
            }
        }

        private void RefreshRunningApplications()
        {
            string? previousSelection = RunningAppsComboBox.SelectedItem as string;
            string? previousLaunchWatchSelection = LaunchWatchAppsComboBox.SelectedItem as string;

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

            runningApps = runningApps
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();

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

        private List<string> GetConfiguredAppsToKill()
        {
            return AppsToKillListBox.Items
                .OfType<string>()
                .Select(NormalizeProcessName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> GetConfiguredAppsToWatchAtLaunch()
        {
            return AppsToWatchAtLaunchListBox.Items
                .OfType<string>()
                .Select(NormalizeProcessName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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
            EthernetWolStatusText.Text = "Checking adapter state...";
            EthernetWolStatusText.Foreground = new SolidColorBrush(Color.FromRgb(184, 184, 184));

            bool? wolEnabled = await Task.Run(() => _systemOps.GetEthernetWakeOnLanEnabled(EthernetAdapterName));

            _isUpdatingEthernetWolState = false;

            if (!wolEnabled.HasValue)
            {
                EthernetWolCheckBox.IsChecked = false;
                EthernetWolCheckBox.IsEnabled = false;
                EthernetWolStatusText.Text = "Unable to read Ethernet power-management state.";
                EthernetWolStatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                return;
            }

            EthernetWolCheckBox.IsChecked = wolEnabled.Value;
            EthernetWolCheckBox.IsEnabled = true;
            UpdateEthernetWolStatusText(wolEnabled.Value);
        }

        private async void EthernetWolCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingEthernetWolState)
            {
                return;
            }

            bool targetState = EthernetWolCheckBox.IsChecked == true;

            EthernetWolCheckBox.IsEnabled = false;
            EthernetWolStatusText.Text = "Applying Wake-on-LAN setting...";
            EthernetWolStatusText.Foreground = new SolidColorBrush(Color.FromRgb(184, 184, 184));

            bool applied = await Task.Run(() => _systemOps.SetEthernetWakeOnLanEnabled(targetState, EthernetAdapterName));
            if (!applied)
            {
                RefreshEthernetWakeOnLanState();
                return;
            }

            RefreshEthernetWakeOnLanState();
        }

        private void UpdateEthernetWolStatusText(bool enabled)
        {
            EthernetWolStatusText.Text = enabled
                ? "Wake-on-LAN is ON for Ethernet."
                : "Wake-on-LAN is OFF for Ethernet.";
            EthernetWolStatusText.Foreground = enabled
                ? new SolidColorBrush(Color.FromRgb(46, 204, 113))
                : new SolidColorBrush(Color.FromRgb(243, 156, 18));
        }

        private void PersistConfiguration(bool showConfirmation)
        {
            _configManager.Config.ShutdownHotkey = ShutdownHotkeyBox.Text;
            _configManager.Config.BlackoutHotkey = BlackoutHotkeyBox.Text;
            _configManager.Config.DarkMode = DarkModeCheckBox.IsChecked ?? true;
            _configManager.Config.StartMinimized = StartMinimizedCheckBox.IsChecked ?? true;
            _configManager.Config.AppsToKillBeforeShutdown = GetConfiguredAppsToKill();
            _configManager.Config.AppsToWatchAfterLaunch = GetConfiguredAppsToWatchAtLaunch();
            _configManager.Config.LaunchWatchDurationMinutes = GetLaunchWatchDurationMinutes();
            _configManager.Config.LaunchMinimizeDelaySeconds = GetLaunchMinimizeDelaySeconds();

            _configManager.SaveConfig();
            _systemOps.SetAppsToKillBeforeShutdown(_configManager.Config.AppsToKillBeforeShutdown);
            _systemOps.SetLaunchWatchConfiguration(
                _configManager.Config.AppsToWatchAfterLaunch,
                _configManager.Config.LaunchWatchDurationMinutes,
                _configManager.Config.LaunchMinimizeDelaySeconds);
            RegisterHotkeys();

            if (showConfirmation)
            {
                MessageBox.Show("Configuration saved successfully!", "System Squire",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            PersistConfiguration(showConfirmation: false);
            e.Cancel = true;
            this.Hide();
            this.ShowInTaskbar = false;
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
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

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            PersistConfiguration(showConfirmation: false);
            _systemOps.StopLaunchWatchWindow();
            _keyboardHook.Dispose();
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            _systemOps.StopLaunchWatchWindow();
            _keyboardHook.Dispose();
            _trayIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}
