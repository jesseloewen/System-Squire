using System;
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

            // Handle minimize to tray
            if (_configManager.Config.StartMinimized)
            {
                this.WindowState = WindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Hide();
            }
        }

        private void LoadConfiguration()
        {
            ShutdownHotkeyBox.Text = _configManager.Config.ShutdownHotkey;
            BlackoutHotkeyBox.Text = _configManager.Config.BlackoutHotkey;
            DarkModeCheckBox.IsChecked = _configManager.Config.DarkMode;
            StartMinimizedCheckBox.IsChecked = _configManager.Config.StartMinimized;

            ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (_configManager.Config.DarkMode)
            {
                this.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            }
            else
            {
                this.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
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
            _configManager.Config.ShutdownHotkey = ShutdownHotkeyBox.Text;
            _configManager.Config.BlackoutHotkey = BlackoutHotkeyBox.Text;
            _configManager.Config.DarkMode = DarkModeCheckBox.IsChecked ?? true;
            _configManager.Config.StartMinimized = StartMinimizedCheckBox.IsChecked ?? true;

            _configManager.SaveConfig();
            RegisterHotkeys();

            MessageBox.Show("Configuration saved successfully!", "System Squire", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
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
            _keyboardHook.Dispose();
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            _keyboardHook.Dispose();
            _trayIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}
