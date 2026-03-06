using System;
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
    }

    /// <summary>
    /// Manages configuration persistence
    /// </summary>
    public class ConfigManager
    {
        private const string ConfigFileName = "config.json";
        private readonly string _configPath;

        public AppConfig Config { get; private set; }

        public ConfigManager()
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
            Config = LoadConfig();
        }

        private AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);
                    return config ?? new AppConfig();
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
                string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }
    }
}
