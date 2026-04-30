using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SystemSquire
{
    /// <summary>
    /// Config entry for a process name with enabled state.
    /// </summary>
    public class ConfiguredAppEntry
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Pushover notification configuration.
    /// </summary>
    public class PushoverConfig
    {
        public class PushoverLifecycleAppEntry
        {
            public string Name { get; set; } = string.Empty;
            public bool NotifyOnStart { get; set; } = true;
            public bool NotifyOnClose { get; set; } = true;
        }

        public class PushoverFolderWatchEntry
        {
            public string FolderPath { get; set; } = string.Empty;
            public bool NotifyOnCreated { get; set; } = true;
            public bool NotifyOnRemoved { get; set; } = true;
            public bool NotifyOnModified { get; set; } = true;
            public bool NotifyOnInactivity { get; set; } = false;
        }

        public bool Enabled { get; set; } = false;
        public string ApiToken { get; set; } = string.Empty;
        public string UserKey { get; set; } = string.Empty;

        public bool NotifyOnSystemSquireStart { get; set; } = false;
        public bool NotifyOnTrackedAppStart { get; set; } = true;
        public bool NotifyOnTrackedAppClose { get; set; } = true;
        public bool NotifyOnSystemSquireClose { get; set; } = false;
        public bool NotifyOnInactivity { get; set; } = false;
        public bool RepeatInactivityNotifications { get; set; } = true;
        public int InactivityNotificationMinutes { get; set; } = 30;
        public int FolderPollingSeconds { get; set; } = 60;
        public int FolderPollingMinutes { get; set; } = 0;
        public int FolderInactivityMinutes { get; set; } = 10;
        public bool RepeatFolderInactivityNotifications { get; set; } = true;

        // Legacy fields are kept for backward compatibility with older configs.
        public List<string> LifecycleAppsToWatch { get; set; } = new();
        public List<ConfiguredAppEntry> LifecycleAppsToWatchEntries { get; set; } = new();
        public List<PushoverLifecycleAppEntry> LifecycleAppEventEntries { get; set; } = new();
        public List<PushoverFolderWatchEntry> FolderWatchEntries { get; set; } = new();

        public void Normalize()
        {
            ApiToken = ApiToken?.Trim() ?? string.Empty;
            UserKey = UserKey?.Trim() ?? string.Empty;
            InactivityNotificationMinutes = Math.Max(1, InactivityNotificationMinutes);
            if (FolderPollingSeconds <= 0)
            {
                FolderPollingSeconds = FolderPollingMinutes > 0
                    ? FolderPollingMinutes * 60
                    : 60;
            }

            FolderPollingSeconds = Math.Max(1, FolderPollingSeconds);
            FolderInactivityMinutes = Math.Max(1, FolderInactivityMinutes);

            var normalizedLegacyEntries = NormalizeConfiguredEntries(
                LifecycleAppsToWatchEntries,
                LifecycleAppsToWatch);

            LifecycleAppEventEntries = NormalizeLifecycleEntries(LifecycleAppEventEntries);
            FolderWatchEntries = NormalizeFolderWatchEntries(FolderWatchEntries);

            if (LifecycleAppEventEntries.Count == 0 && normalizedLegacyEntries.Count > 0)
            {
                LifecycleAppEventEntries = normalizedLegacyEntries
                    .Where(entry => entry.Enabled)
                    .Select(entry => new PushoverLifecycleAppEntry
                    {
                        Name = entry.Name,
                        NotifyOnStart = NotifyOnTrackedAppStart,
                        NotifyOnClose = NotifyOnTrackedAppClose
                    })
                    .ToList();

                LifecycleAppEventEntries = NormalizeLifecycleEntries(LifecycleAppEventEntries);
            }

            LifecycleAppsToWatchEntries = LifecycleAppEventEntries
                .Select(entry => new ConfiguredAppEntry
                {
                    Name = entry.Name,
                    Enabled = entry.NotifyOnStart || entry.NotifyOnClose
                })
                .ToList();

            LifecycleAppsToWatch = LifecycleAppsToWatchEntries
                .Where(entry => entry.Enabled)
                .Select(entry => entry.Name)
                .ToList();
        }

        private static List<ConfiguredAppEntry> NormalizeConfiguredEntries(
            IEnumerable<ConfiguredAppEntry>? entries,
            IEnumerable<string>? legacyNames)
        {
            var normalized = entries?
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => new ConfiguredAppEntry
                {
                    Name = NormalizeProcessName(entry.Name),
                    Enabled = entry.Enabled
                })
                .ToList() ?? new List<ConfiguredAppEntry>();

            if (normalized.Count == 0)
            {
                normalized = legacyNames?
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => new ConfiguredAppEntry
                    {
                        Name = NormalizeProcessName(name),
                        Enabled = true
                    })
                    .ToList() ?? new List<ConfiguredAppEntry>();
            }

            return normalized
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ConfiguredAppEntry
                {
                    Name = group.First().Name,
                    Enabled = group.Any(entry => entry.Enabled)
                })
                .ToList();
        }

        private static List<PushoverLifecycleAppEntry> NormalizeLifecycleEntries(
            IEnumerable<PushoverLifecycleAppEntry>? entries)
        {
            var normalized = entries?
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => new PushoverLifecycleAppEntry
                {
                    Name = NormalizeProcessName(entry.Name),
                    NotifyOnStart = entry.NotifyOnStart,
                    NotifyOnClose = entry.NotifyOnClose
                })
                .ToList() ?? new List<PushoverLifecycleAppEntry>();

            return normalized
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PushoverLifecycleAppEntry
                {
                    Name = group.First().Name,
                    NotifyOnStart = group.Any(entry => entry.NotifyOnStart),
                    NotifyOnClose = group.Any(entry => entry.NotifyOnClose)
                })
                .ToList();
        }

        private static string NormalizeProcessName(string value)
        {
            string trimmed = value.Trim();
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^4]
                : trimmed;
        }

        private static List<PushoverFolderWatchEntry> NormalizeFolderWatchEntries(
            IEnumerable<PushoverFolderWatchEntry>? entries)
        {
            var normalized = entries?
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FolderPath))
                .Select(entry => new PushoverFolderWatchEntry
                {
                    FolderPath = NormalizeFolderPath(entry.FolderPath),
                    NotifyOnCreated = entry.NotifyOnCreated,
                    NotifyOnRemoved = entry.NotifyOnRemoved,
                    NotifyOnModified = entry.NotifyOnModified,
                    NotifyOnInactivity = entry.NotifyOnInactivity
                })
                .ToList() ?? new List<PushoverFolderWatchEntry>();

            return normalized
                .GroupBy(entry => entry.FolderPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PushoverFolderWatchEntry
                {
                    FolderPath = group.First().FolderPath,
                    NotifyOnCreated = group.Any(entry => entry.NotifyOnCreated),
                    NotifyOnRemoved = group.Any(entry => entry.NotifyOnRemoved),
                    NotifyOnModified = group.Any(entry => entry.NotifyOnModified),
                    NotifyOnInactivity = group.Any(entry => entry.NotifyOnInactivity)
                })
                .ToList();
        }

        private static string NormalizeFolderPath(string value)
        {
            return value.Trim();
        }
    }

    /// <summary>
    /// Application configuration storage
    /// </summary>
    public class AppConfig
    {
        public string ShutdownHotkey { get; set; } = "Ctrl+Alt+F8";
        public string BlackoutHotkey { get; set; } = "Ctrl+Alt+F7";
        public bool StartMinimized { get; set; } = true;
        public int WebServicePort { get; set; } = 7745;
        public bool WebServiceAutoStart { get; set; } = false;
        public bool AutoOpenWebPageOnStartup { get; set; } = false;
        public bool WebServiceRequirePassword { get; set; } = false;
        public string WebServicePasswordHash { get; set; } = string.Empty;
        public string WebServicePasswordSalt { get; set; } = string.Empty;

        // Legacy string lists are kept for backward compatibility with old config files.
        public List<string> AppsToKillBeforeShutdown { get; set; } = new();
        public List<string> AppsToWatchAfterLaunch { get; set; } = new();

        public List<ConfiguredAppEntry> AppsToKillBeforeShutdownEntries { get; set; } = new();
        public List<ConfiguredAppEntry> AppsToWatchAfterLaunchEntries { get; set; } = new();

        public int LaunchWatchDurationMinutes { get; set; } = 1;
        public int LaunchMinimizeDelaySeconds { get; set; } = 0;
        public bool? EthernetWakeOnLanEnabled { get; set; }
        public PushoverConfig Pushover { get; set; } = new();

        public void Normalize()
        {
            WebServicePort = NormalizePort(WebServicePort);
            WebServicePasswordHash = WebServicePasswordHash?.Trim() ?? string.Empty;
            WebServicePasswordSalt = WebServicePasswordSalt?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(WebServicePasswordHash) ||
                string.IsNullOrWhiteSpace(WebServicePasswordSalt))
            {
                WebServiceRequirePassword = false;
            }

            AppsToKillBeforeShutdownEntries = NormalizeEntries(
                AppsToKillBeforeShutdownEntries,
                AppsToKillBeforeShutdown);

            AppsToWatchAfterLaunchEntries = NormalizeEntries(
                AppsToWatchAfterLaunchEntries,
                AppsToWatchAfterLaunch);

            AppsToKillBeforeShutdown = AppsToKillBeforeShutdownEntries
                .Where(entry => entry.Enabled)
                .Select(entry => entry.Name)
                .ToList();

            AppsToWatchAfterLaunch = AppsToWatchAfterLaunchEntries
                .Where(entry => entry.Enabled)
                .Select(entry => entry.Name)
                .ToList();

            Pushover ??= new PushoverConfig();
            Pushover.Normalize();
        }

        private static List<ConfiguredAppEntry> NormalizeEntries(
            IEnumerable<ConfiguredAppEntry>? entries,
            IEnumerable<string>? legacyNames)
        {
            var normalized = entries?
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => new ConfiguredAppEntry
                {
                    Name = NormalizeProcessName(entry.Name),
                    Enabled = entry.Enabled
                })
                .ToList() ?? new List<ConfiguredAppEntry>();

            if (normalized.Count == 0)
            {
                normalized = legacyNames?
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => new ConfiguredAppEntry
                    {
                        Name = NormalizeProcessName(name),
                        Enabled = true
                    })
                    .ToList() ?? new List<ConfiguredAppEntry>();
            }

            return normalized
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ConfiguredAppEntry
                {
                    Name = group.First().Name,
                    Enabled = group.Any(entry => entry.Enabled)
                })
                .ToList();
        }

        private static string NormalizeProcessName(string value)
        {
            string trimmed = value.Trim();
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^4]
                : trimmed;
        }

        private static int NormalizePort(int port)
        {
            return port is >= 1 and <= 65535 ? port : 7745;
        }
    }

    /// <summary>
    /// Manages configuration persistence
    /// </summary>
    public class ConfigManager
    {
        private const string ConfigFileName = "config.json";
        private const string AppFolderName = "SystemSquire";
        private readonly string _configPath;
        private readonly string _legacyConfigPath;

        public AppConfig Config { get; private set; }

        public ConfigManager()
        {
            string appDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);

            _configPath = Path.Combine(appDataDirectory, ConfigFileName);
            _legacyConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
            Config = LoadConfig();
        }

        private AppConfig LoadConfig()
        {
            try
            {
                if (TryReadConfig(_configPath, out AppConfig appDataConfig))
                {
                    return appDataConfig;
                }

                // Migrate legacy config that was previously stored beside the executable.
                if (TryReadConfig(_legacyConfigPath, out AppConfig legacyConfig))
                {
                    Config = legacyConfig;
                    SaveConfig();
                    return legacyConfig;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
            }

            return new AppConfig();
        }

        public void SaveConfig()
        {
            try
            {
                Config.Normalize();

                string? configDirectory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrWhiteSpace(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                }

                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        private static bool TryReadConfig(string path, out AppConfig config)
        {
            config = new AppConfig();

            if (!File.Exists(path))
            {
                return false;
            }

            string json = File.ReadAllText(path);
            AppConfig? parsed = JsonConvert.DeserializeObject<AppConfig>(json);
            if (parsed == null)
            {
                return false;
            }

            parsed.Normalize();
            config = parsed;
            return true;
        }
    }
}
