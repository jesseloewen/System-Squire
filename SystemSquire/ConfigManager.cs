using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SystemSquire
{
    /// <summary>
    /// Application configuration storage
    /// </summary>
    public class AppConfig
    {
        public string ShutdownHotkey { get; set; } = "Ctrl+Alt+F8";
        public string BlackoutHotkey { get; set; } = "Ctrl+Alt+F7";
        public bool DarkMode { get; set; } = true;
        public bool StartMinimized { get; set; } = true;
        public List<string> AppsToKillBeforeShutdown { get; set; } = new();
        public List<string> AppsToWatchAfterLaunch { get; set; } = new();
        public int LaunchWatchDurationMinutes { get; set; } = 1;
        public int LaunchMinimizeDelaySeconds { get; set; } = 0;
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

            config = parsed;
            return true;
        }
    }
}
