using System.Collections.Generic;

namespace SystemSquire
{
    public sealed class RemoteControlState
    {
        public string StatusText { get; set; } = "Ready";
        public string ShutdownHotkey { get; set; } = "Ctrl+Alt+F8";
        public string BlackoutHotkey { get; set; } = "Ctrl+Alt+F7";
        public bool StartMinimized { get; set; } = true;
        public int LaunchWatchDurationMinutes { get; set; } = 1;
        public int LaunchMinimizeDelaySeconds { get; set; } = 0;
        public List<ConfiguredAppEntry> AppsToKillBeforeShutdown { get; set; } = new();
        public List<ConfiguredAppEntry> AppsToWatchAfterLaunch { get; set; } = new();
        public List<string> RunningApplications { get; set; } = new();
        public PushoverConfig Pushover { get; set; } = new();
        public bool WebServiceRunning { get; set; }
        public int WebServicePort { get; set; } = 7745;
    }

    public sealed class RemoteConfigUpdateRequest
    {
        public string? ShutdownHotkey { get; set; }
        public string? BlackoutHotkey { get; set; }
        public bool StartMinimized { get; set; } = true;
        public int LaunchWatchDurationMinutes { get; set; } = 1;
        public int LaunchMinimizeDelaySeconds { get; set; }
        public List<ConfiguredAppEntry> AppsToKillBeforeShutdown { get; set; } = new();
        public List<ConfiguredAppEntry> AppsToWatchAfterLaunch { get; set; } = new();
        public PushoverConfig? Pushover { get; set; }
    }

    public sealed class RemoteWebAuthSettings
    {
        public bool RequirePassword { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
    }

    public sealed class RemoteLoginRequest
    {
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = true;
    }

    public sealed class RemoteAuthStatusResponse
    {
        public bool AuthenticationRequired { get; set; }
        public bool Authenticated { get; set; }
    }

    public sealed class RemoteOperationResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public RemoteControlState? State { get; set; }
    }
}
