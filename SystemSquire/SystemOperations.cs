using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.ComponentModel;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;

namespace SystemSquire
{
    public enum AppLifecycleEventType
    {
        Started,
        Closed
    }

    public sealed class AppLifecycleEventArgs : EventArgs
    {
        public string AppName { get; }
        public AppLifecycleEventType EventType { get; }

        public AppLifecycleEventArgs(string appName, AppLifecycleEventType eventType)
        {
            AppName = appName;
            EventType = eventType;
        }
    }

    public sealed class InactivityEventArgs : EventArgs
    {
        public TimeSpan IdleDuration { get; }
        public TimeSpan NotificationInterval { get; }

        public InactivityEventArgs(TimeSpan idleDuration, TimeSpan notificationInterval)
        {
            IdleDuration = idleDuration;
            NotificationInterval = notificationInterval;
        }
    }

    public enum FolderWatchEventType
    {
        Created,
        Removed,
        Modified,
        Inactive
    }

    public enum ElevatedOperationResult
    {
        Success,
        Cancelled,
        Failed
    }

    public sealed class FolderWatchConfigEntry
    {
        public string FolderPath { get; set; } = string.Empty;
        public bool NotifyOnCreated { get; set; } = true;
        public bool NotifyOnRemoved { get; set; } = true;
        public bool NotifyOnModified { get; set; } = true;
        public bool NotifyOnInactivity { get; set; } = false;
    }

    public sealed class FolderWatchEventArgs : EventArgs
    {
        public string FolderPath { get; }
        public string RelativePath { get; }
        public FolderWatchEventType EventType { get; }
        public TimeSpan InactivityDuration { get; }

        public FolderWatchEventArgs(
            string folderPath,
            string relativePath,
            FolderWatchEventType eventType,
            TimeSpan inactivityDuration)
        {
            FolderPath = folderPath;
            RelativePath = relativePath;
            EventType = eventType;
            InactivityDuration = inactivityDuration;
        }
    }

    /// <summary>
    /// Handles system operations (shutdown, monitor control)
    /// </summary>
    public class SystemOperations
    {
        private const string WrappedPowerShellErrorPrefix = "__SYSTEM_SQUIRE_ERROR__";
        private bool _shutdownActive = false;
        private bool _cooldownActive = false;
        private CancellationTokenSource? _shutdownCts;
        private List<string> _appsToKillBeforeShutdown = new();
        private List<string> _appsToWatchAfterLaunch = new();
        private TimeSpan _launchWatchDuration = TimeSpan.FromMinutes(1);
        private TimeSpan _launchMinimizeDelay = TimeSpan.Zero;
        private CancellationTokenSource? _launchWatchCts;
        private List<string> _appsToWatchForLifecycleNotifications = new();
        private CancellationTokenSource? _appLifecycleWatchCts;
        private TimeSpan _inactivityNotificationInterval = TimeSpan.FromMinutes(30);
        private bool _repeatInactivityNotifications = true;
        private CancellationTokenSource? _inactivityWatchCts;
        private List<FolderWatchConfigEntry> _folderWatchEntries = new();
        private TimeSpan _folderPollingInterval = TimeSpan.FromSeconds(60);
        private TimeSpan _folderInactivityInterval = TimeSpan.FromMinutes(10);
        private bool _repeatFolderInactivityNotifications = true;
        private CancellationTokenSource? _folderWatchCts;

        private sealed class FileSnapshotInfo
        {
            public DateTime LastWriteUtc { get; set; }
            public long Length { get; set; }
        }

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<AppLifecycleEventArgs>? AppLifecycleEventDetected;
        public event EventHandler<InactivityEventArgs>? InactivityDetected;
        public event EventHandler<FolderWatchEventArgs>? FolderWatchEventDetected;

        public void SetAppsToKillBeforeShutdown(IEnumerable<string>? processNames)
        {
            _appsToKillBeforeShutdown = processNames?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        public void SetLaunchWatchConfiguration(IEnumerable<string>? processNames, int durationMinutes, int minimizeDelaySeconds)
        {
            SetAppsToWatchAfterLaunch(processNames);
            SetLaunchWatchDurationMinutes(durationMinutes);
            SetLaunchMinimizeDelaySeconds(minimizeDelaySeconds);
        }

        public void SetAppsToWatchAfterLaunch(IEnumerable<string>? processNames)
        {
            _appsToWatchAfterLaunch = processNames?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        public void SetLaunchWatchDurationMinutes(int durationMinutes)
        {
            int safeMinutes = Math.Max(1, durationMinutes);
            _launchWatchDuration = TimeSpan.FromMinutes(safeMinutes);
        }

        public void SetLaunchMinimizeDelaySeconds(int delaySeconds)
        {
            int safeDelaySeconds = Math.Max(0, delaySeconds);
            _launchMinimizeDelay = TimeSpan.FromSeconds(safeDelaySeconds);
        }

        public void SetAppsToWatchForLifecycleNotifications(IEnumerable<string>? processNames)
        {
            _appsToWatchForLifecycleNotifications = processNames?
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        public void StartAppLifecycleWatch()
        {
            StopAppLifecycleWatch();

            if (_appsToWatchForLifecycleNotifications.Count == 0)
            {
                return;
            }

            _appLifecycleWatchCts = new CancellationTokenSource();
            _ = Task.Run(() => WatchForAppLifecycleAsync(_appLifecycleWatchCts.Token));
        }

        public void StopAppLifecycleWatch()
        {
            if (_appLifecycleWatchCts == null)
            {
                return;
            }

            _appLifecycleWatchCts.Cancel();
            _appLifecycleWatchCts.Dispose();
            _appLifecycleWatchCts = null;
        }

        public void ConfigureInactivityWatch(bool enabled, int inactivityMinutes, bool repeatNotifications)
        {
            StopInactivityWatch();

            if (!enabled)
            {
                return;
            }

            _inactivityNotificationInterval = TimeSpan.FromMinutes(Math.Max(1, inactivityMinutes));
            _repeatInactivityNotifications = repeatNotifications;
            _inactivityWatchCts = new CancellationTokenSource();
            _ = Task.Run(() => WatchForInactivityAsync(_inactivityWatchCts.Token));
        }

        public void StopInactivityWatch()
        {
            if (_inactivityWatchCts == null)
            {
                return;
            }

            _inactivityWatchCts.Cancel();
            _inactivityWatchCts.Dispose();
            _inactivityWatchCts = null;
        }

        public void SetFolderWatchConfiguration(
            IEnumerable<FolderWatchConfigEntry>? entries,
            int pollingSeconds,
            int inactivityMinutes,
            bool repeatInactivityNotifications)
        {
            _folderWatchEntries = entries?
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FolderPath))
                .Select(entry => new FolderWatchConfigEntry
                {
                    FolderPath = entry.FolderPath.Trim(),
                    NotifyOnCreated = entry.NotifyOnCreated,
                    NotifyOnRemoved = entry.NotifyOnRemoved,
                    NotifyOnModified = entry.NotifyOnModified,
                    NotifyOnInactivity = entry.NotifyOnInactivity
                })
                .GroupBy(entry => entry.FolderPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => new FolderWatchConfigEntry
                {
                    FolderPath = group.First().FolderPath,
                    NotifyOnCreated = group.Any(entry => entry.NotifyOnCreated),
                    NotifyOnRemoved = group.Any(entry => entry.NotifyOnRemoved),
                    NotifyOnModified = group.Any(entry => entry.NotifyOnModified),
                    NotifyOnInactivity = group.Any(entry => entry.NotifyOnInactivity)
                })
                .ToList() ?? new List<FolderWatchConfigEntry>();

            _folderPollingInterval = TimeSpan.FromSeconds(Math.Max(1, pollingSeconds));
            _folderInactivityInterval = TimeSpan.FromMinutes(Math.Max(1, inactivityMinutes));
            _repeatFolderInactivityNotifications = repeatInactivityNotifications;
        }

        public void StartFolderWatch()
        {
            StopFolderWatch();

            if (_folderWatchEntries.Count == 0)
            {
                return;
            }

            _folderWatchCts = new CancellationTokenSource();
            _ = Task.Run(() => WatchFoldersAsync(_folderWatchCts.Token));
        }

        public void StopFolderWatch()
        {
            if (_folderWatchCts == null)
            {
                return;
            }

            _folderWatchCts.Cancel();
            _folderWatchCts.Dispose();
            _folderWatchCts = null;
        }

        public void StartLaunchWatchWindow()
        {
            StopLaunchWatchWindow();

            if (_appsToWatchAfterLaunch.Count == 0)
            {
                return;
            }

            _launchWatchCts = new CancellationTokenSource();
            _ = Task.Run(() => WatchForAppLaunchesAsync(_launchWatchCts.Token));
        }

        public void StopLaunchWatchWindow()
        {
            if (_launchWatchCts == null)
            {
                return;
            }

            _launchWatchCts.Cancel();
            _launchWatchCts.Dispose();
            _launchWatchCts = null;
        }

        public async void TriggerShutdown()
        {
            if (_cooldownActive)
            {
                OnStatusChanged("Cooldown active - please wait");
                return;
            }

            if (_shutdownActive)
            {
                // Cancel shutdown
                await CancelShutdown();
            }
            else
            {
                // Start shutdown
                await StartShutdown();
            }
        }

        private async Task StartShutdown()
        {
            _shutdownActive = true;
            _shutdownCts = new CancellationTokenSource();
            
            try
            {
                int killedCount = KillConfiguredApplications();

                OnStatusChanged("Shutdown initiated - 10 seconds");
                if (killedCount > 0)
                {
                    OnStatusChanged($"Killed {killedCount} app process(es), shutdown in 10 seconds");
                }
                
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/sg /t 10",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                
                Process.Start(psi);
                
                // Wait for potential cancellation
                await Task.Delay(10000, _shutdownCts.Token);
            }
            catch (TaskCanceledException)
            {
                // Shutdown was cancelled
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error: {ex.Message}");
            }
            finally
            {
                _shutdownActive = false;
            }
        }

        private async Task CancelShutdown()
        {
            try
            {
                _shutdownCts?.Cancel();
                
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/a",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                
                Process.Start(psi);
                
                OnStatusChanged("Shutdown cancelled");
                
                // Start cooldown
                await StartCooldown();
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error: {ex.Message}");
            }
            finally
            {
                _shutdownActive = false;
            }
        }

        private async Task StartCooldown()
        {
            _cooldownActive = true;
            OnStatusChanged("Cooldown active (5 seconds)");
            await Task.Delay(5000);
            _cooldownActive = false;
            OnStatusChanged("Ready");
        }

        public void TriggerBlackout()
        {
            try
            {
                OnStatusChanged("Blackout activated");
                
                // Turn off monitors
                const int SC_MONITORPOWER = 0xF170;
                const int MONITOR_OFF = 2;
                
                SendMessage(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)MONITOR_OFF);
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error: {ex.Message}");
            }
        }

        public bool TriggerDesktopLock()
        {
            try
            {
                bool locked = LockWorkStation();
                if (locked)
                {
                    OnStatusChanged("Desktop locked");
                    return true;
                }

                OnStatusChanged("Error: Failed to lock desktop");
                return false;
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error: {ex.Message}");
                return false;
            }
        }

        public bool? GetEthernetWakeOnLanEnabled(string adapterName = "Ethernet")
        {
            try
            {
                string escapedAdapterName = EscapeForSingleQuotedPowerShellString(adapterName);
                string command = $"$pm = Get-NetAdapterPowerManagement -Name '{escapedAdapterName}' -ErrorAction Stop; $pm.WakeOnMagicPacket";

                if (!TryRunPowerShellCommand(command, out string output, out string errorMessage))
                {
                    OnStatusChanged($"Error: {errorMessage}");
                    return null;
                }

                string state = output.Trim();
                if (string.Equals(state, "Enabled", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(state, "Disabled", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                OnStatusChanged($"Warning: Unexpected WoL state for {adapterName}: {state}");
                return null;
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error: {ex.Message}");
                return null;
            }
        }

        public ElevatedOperationResult SetEthernetWakeOnLanEnabled(bool enabled, string adapterName = "Ethernet")
        {
            try
            {
                string targetState = enabled ? "Enabled" : "Disabled";
                string escapedAdapterName = EscapeForSingleQuotedPowerShellString(adapterName);
                string command =
                    $"Set-NetAdapterPowerManagement -Name '{escapedAdapterName}' -WakeOnMagicPacket {targetState} -ErrorAction Stop; " +
                    $"(Get-NetAdapterPowerManagement -Name '{escapedAdapterName}' -ErrorAction Stop).WakeOnMagicPacket";

                if (!TryRunElevatedPowerShellCommand(command, out string errorMessage, out bool wasCancelled))
                {
                    if (wasCancelled)
                    {
                        OnStatusChanged("Wake-on-LAN change canceled because administrator approval was not granted.");
                        return ElevatedOperationResult.Cancelled;
                    }

                    OnStatusChanged($"Error: {errorMessage}");
                    return ElevatedOperationResult.Failed;
                }

                OnStatusChanged($"Wake-on-LAN for {adapterName} set to {targetState}");
                return ElevatedOperationResult.Success;
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error: {ex.Message}");
                return ElevatedOperationResult.Failed;
            }
        }

        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        private static bool TryRunPowerShellCommand(string command, out string output, out string errorMessage)
        {
            output = string.Empty;
            errorMessage = string.Empty;

            try
            {
                string wrappedCommand = BuildWrappedPowerShellCommand(command);
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrappedCommand));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(psi);
                if (process == null)
                {
                    errorMessage = "Failed to start PowerShell process.";
                    return false;
                }

                output = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (TryExtractWrappedPowerShellError(output, out string wrappedError))
                {
                    errorMessage = wrappedError;
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    string readableError = SanitizePowerShellErrorText(errorOutput);
                    errorMessage = string.IsNullOrWhiteSpace(readableError)
                        ? $"PowerShell exited with code {process.ExitCode}."
                        : readableError;
                    return false;
                }

                output = output.Trim();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static bool TryRunElevatedPowerShellCommand(string command, out string errorMessage, out bool wasCancelled)
        {
            errorMessage = string.Empty;
            wasCancelled = false;

            try
            {
                string wrappedCommand = BuildWrappedPowerShellCommand(command);
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrappedCommand));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encodedCommand}",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using Process? process = Process.Start(psi);
                if (process == null)
                {
                    errorMessage = "Failed to start elevated PowerShell process.";
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    errorMessage = "PowerShell command failed while applying Wake-on-LAN settings.";
                    return false;
                }

                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                wasCancelled = true;
                errorMessage = "Administrator approval was canceled.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string EscapeForSingleQuotedPowerShellString(string value)
        {
            return value.Replace("'", "''");
        }

        private static string BuildWrappedPowerShellCommand(string command)
        {
            return "$ProgressPreference='SilentlyContinue';" +
                   "$VerbosePreference='SilentlyContinue';" +
                   "$InformationPreference='SilentlyContinue';" +
                   "$WarningPreference='SilentlyContinue';" +
                   "$ErrorActionPreference='Stop';" +
                   "try {" +
                   command +
                   "} catch {" +
                   $"Write-Output ('{WrappedPowerShellErrorPrefix}' + $_.Exception.Message);" +
                   "exit 1;" +
                   "}";
        }

        private static bool TryExtractWrappedPowerShellError(string output, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (!line.StartsWith(WrappedPowerShellErrorPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                errorMessage = line[WrappedPowerShellErrorPrefix.Length..].Trim();
                if (string.IsNullOrWhiteSpace(errorMessage))
                {
                    errorMessage = "PowerShell command failed.";
                }

                return true;
            }

            return false;
        }

        private static string SanitizePowerShellErrorText(string rawError)
        {
            if (string.IsNullOrWhiteSpace(rawError))
            {
                return string.Empty;
            }

            string cleaned = rawError.Trim();

            if (cleaned.StartsWith("#< CLIXML", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Replace("#< CLIXML", string.Empty, StringComparison.OrdinalIgnoreCase);
                cleaned = Regex.Replace(cleaned, "<[^>]+>", " ");
                cleaned = WebUtility.HtmlDecode(cleaned);
                cleaned = cleaned.Replace("_x000D__x000A_", " ", StringComparison.OrdinalIgnoreCase);
                cleaned = cleaned.Replace("_x000D_", " ", StringComparison.OrdinalIgnoreCase);
                cleaned = cleaned.Replace("_x000A_", " ", StringComparison.OrdinalIgnoreCase);
                cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();

                if (cleaned.Contains("Preparing modules for first use", StringComparison.OrdinalIgnoreCase))
                {
                    return "PowerShell is preparing networking modules. Try toggling Wake-on-LAN again.";
                }
            }

            return cleaned;
        }

        private int KillConfiguredApplications()
        {
            int killedCount = 0;

            foreach (string processName in _appsToKillBeforeShutdown)
            {
                try
                {
                    foreach (var process in Process.GetProcessesByName(processName))
                    {
                        try
                        {
                            if (process.HasExited)
                            {
                                continue;
                            }

                            process.Kill(true);
                            process.WaitForExit(2000);
                            killedCount++;
                        }
                        catch
                        {
                            // Best effort: continue shutting down even if one process fails to close.
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch
                {
                    // Best effort: continue shutdown even when a process name cannot be queried.
                }
            }

            return killedCount;
        }

        private async Task WatchForAppLaunchesAsync(CancellationToken cancellationToken)
        {
            List<string> appsToWatch = _appsToWatchAfterLaunch.ToList();
            if (appsToWatch.Count == 0)
            {
                return;
            }

            TimeSpan watchDuration = _launchWatchDuration;
            DateTime watchUntil = DateTime.UtcNow.Add(watchDuration);

            var knownProcessIdsByApp = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (string appName in appsToWatch)
            {
                knownProcessIdsByApp[appName] = CaptureProcessIds(appName);
            }

            OnStatusChanged($"Watching launch apps for {watchDuration.TotalMinutes:0.#} minute(s)");

            try
            {
                string? detectedAppName = null;

                while (DateTime.UtcNow < watchUntil && detectedAppName == null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (string appName in appsToWatch)
                    {
                        HashSet<int> knownProcessIds = knownProcessIdsByApp[appName];

                        foreach (Process process in Process.GetProcessesByName(appName))
                        {
                            try
                            {
                                if (process.HasExited)
                                {
                                    continue;
                                }

                                if (knownProcessIds.Contains(process.Id))
                                {
                                    continue;
                                }

                                if (process.MainWindowHandle == IntPtr.Zero)
                                {
                                    continue;
                                }

                                knownProcessIds.Add(process.Id);

                                detectedAppName = appName;
                                OnStatusChanged($"Detected window for {appName}; waiting {_launchMinimizeDelay.TotalSeconds:0} second(s) before minimize");
                                break;
                            }
                            catch
                            {
                                // Best effort: continue monitoring other processes.
                            }
                            finally
                            {
                                process.Dispose();
                            }
                        }

                        if (detectedAppName != null)
                        {
                            break;
                        }
                    }

                    if (detectedAppName == null)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                }

                if (detectedAppName == null)
                {
                    OnStatusChanged("Launch watch window ended");
                    return;
                }

                if (_launchMinimizeDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_launchMinimizeDelay, cancellationToken);
                }

                if (MinimizeToTray(detectedAppName))
                {
                    OnStatusChanged($"Auto-minimized {detectedAppName}");
                }
                else
                {
                    OnStatusChanged($"Warning: Failed to auto-minimize {detectedAppName}");
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when watcher is restarted or application exits.
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Warning: Launch watch error - {ex.Message}");
            }
        }

        private static HashSet<int> CaptureProcessIds(string processName)
        {
            var processIds = new HashSet<int>();

            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        processIds.Add(process.Id);
                    }
                }
                catch
                {
                    // Ignore inaccessible or terminating processes.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return processIds;
        }

        private async Task WatchForAppLifecycleAsync(CancellationToken cancellationToken)
        {
            try
            {
                var runningStateByApp = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                foreach (string appName in _appsToWatchForLifecycleNotifications)
                {
                    runningStateByApp[appName] = IsProcessRunning(appName);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    foreach (string appName in _appsToWatchForLifecycleNotifications)
                    {
                        bool wasRunning = runningStateByApp.TryGetValue(appName, out bool priorState) && priorState;
                        bool isRunning = IsProcessRunning(appName);

                        if (!wasRunning && isRunning)
                        {
                            runningStateByApp[appName] = true;
                            OnAppLifecycleEventDetected(new AppLifecycleEventArgs(appName, AppLifecycleEventType.Started));
                        }
                        else if (wasRunning && !isRunning)
                        {
                            runningStateByApp[appName] = false;
                            OnAppLifecycleEventDetected(new AppLifecycleEventArgs(appName, AppLifecycleEventType.Closed));
                        }
                    }

                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when watcher is stopped.
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Warning: App lifecycle watch error - {ex.Message}");
            }
        }

        private async Task WatchForInactivityAsync(CancellationToken cancellationToken)
        {
            try
            {
                int lastNotifiedPeriodCount = 0;
                bool inactivityAlertSent = false;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!TryGetIdleDuration(out TimeSpan idleDuration))
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    if (idleDuration < _inactivityNotificationInterval)
                    {
                        lastNotifiedPeriodCount = 0;
                        inactivityAlertSent = false;
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    if (!_repeatInactivityNotifications)
                    {
                        if (!inactivityAlertSent)
                        {
                            inactivityAlertSent = true;
                            OnInactivityDetected(new InactivityEventArgs(idleDuration, _inactivityNotificationInterval));
                        }
                    }
                    else
                    {
                        int elapsedPeriods = (int)(idleDuration.TotalMilliseconds / _inactivityNotificationInterval.TotalMilliseconds);
                        if (elapsedPeriods > lastNotifiedPeriodCount)
                        {
                            lastNotifiedPeriodCount = elapsedPeriods;
                            OnInactivityDetected(new InactivityEventArgs(idleDuration, _inactivityNotificationInterval));
                        }
                    }

                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when watcher is stopped.
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Warning: Inactivity watch error - {ex.Message}");
            }
        }

        private static bool TryGetIdleDuration(out TimeSpan idleDuration)
        {
            idleDuration = TimeSpan.Zero;

            var lastInputInfo = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
            };

            if (!GetLastInputInfo(ref lastInputInfo))
            {
                return false;
            }

            uint tickCount = unchecked((uint)Environment.TickCount);
            uint idleMilliseconds = unchecked(tickCount - lastInputInfo.dwTime);
            idleDuration = TimeSpan.FromMilliseconds(idleMilliseconds);
            return true;
        }

        private static bool IsProcessRunning(string processName)
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore inaccessible process details.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore process-query failures.
            }

            return false;
        }

        private async Task WatchFoldersAsync(CancellationToken cancellationToken)
        {
            try
            {
                var lastSnapshotsByFolder = new Dictionary<string, Dictionary<string, FileSnapshotInfo>>(StringComparer.OrdinalIgnoreCase);
                var lastChangeUtcByFolder = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                var inactivityPeriodsByFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (FolderWatchConfigEntry entry in _folderWatchEntries)
                {
                    Dictionary<string, FileSnapshotInfo> snapshot = CaptureFolderSnapshot(entry.FolderPath);
                    lastSnapshotsByFolder[entry.FolderPath] = snapshot;
                    lastChangeUtcByFolder[entry.FolderPath] = snapshot.Count > 0
                        ? snapshot.Values.Max(info => info.LastWriteUtc)
                        : DateTime.UtcNow;
                    inactivityPeriodsByFolder[entry.FolderPath] = 0;
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    DateTime nowUtc = DateTime.UtcNow;

                    foreach (FolderWatchConfigEntry entry in _folderWatchEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        Dictionary<string, FileSnapshotInfo> previousSnapshot = lastSnapshotsByFolder[entry.FolderPath];
                        Dictionary<string, FileSnapshotInfo> currentSnapshot = CaptureFolderSnapshot(entry.FolderPath);

                        var createdFiles = currentSnapshot.Keys
                            .Except(previousSnapshot.Keys, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var removedFiles = previousSnapshot.Keys
                            .Except(currentSnapshot.Keys, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        var modifiedFiles = currentSnapshot.Keys
                            .Intersect(previousSnapshot.Keys, StringComparer.OrdinalIgnoreCase)
                            .Where(path => !AreSnapshotsEqual(previousSnapshot[path], currentSnapshot[path]))
                            .ToList();

                        bool anyChanges = createdFiles.Count > 0 || removedFiles.Count > 0 || modifiedFiles.Count > 0;

                        bool wasInactive = entry.NotifyOnInactivity && inactivityPeriodsByFolder[entry.FolderPath] > 0;

                        if (anyChanges)
                        {
                            if (wasInactive)
                            {
                                string resumedPath = modifiedFiles.FirstOrDefault()
                                    ?? createdFiles.FirstOrDefault()
                                    ?? removedFiles.FirstOrDefault()
                                    ?? string.Empty;

                                OnFolderWatchEventDetected(new FolderWatchEventArgs(
                                    entry.FolderPath,
                                    string.IsNullOrWhiteSpace(resumedPath)
                                        ? "Activity resumed"
                                        : GetRelativePathSafe(entry.FolderPath, resumedPath),
                                    FolderWatchEventType.Modified,
                                    TimeSpan.Zero));
                            }
                            else
                            {
                                if (entry.NotifyOnCreated)
                                {
                                    foreach (string filePath in createdFiles)
                                    {
                                        OnFolderWatchEventDetected(new FolderWatchEventArgs(
                                            entry.FolderPath,
                                            GetRelativePathSafe(entry.FolderPath, filePath),
                                            FolderWatchEventType.Created,
                                            TimeSpan.Zero));
                                    }
                                }

                                if (entry.NotifyOnRemoved)
                                {
                                    foreach (string filePath in removedFiles)
                                    {
                                        OnFolderWatchEventDetected(new FolderWatchEventArgs(
                                            entry.FolderPath,
                                            GetRelativePathSafe(entry.FolderPath, filePath),
                                            FolderWatchEventType.Removed,
                                            TimeSpan.Zero));
                                    }
                                }

                                if (entry.NotifyOnModified)
                                {
                                    foreach (string filePath in modifiedFiles)
                                    {
                                        OnFolderWatchEventDetected(new FolderWatchEventArgs(
                                            entry.FolderPath,
                                            GetRelativePathSafe(entry.FolderPath, filePath),
                                            FolderWatchEventType.Modified,
                                            TimeSpan.Zero));
                                    }
                                }
                            }

                            lastChangeUtcByFolder[entry.FolderPath] = nowUtc;
                            inactivityPeriodsByFolder[entry.FolderPath] = 0;
                        }
                        else if (entry.NotifyOnInactivity)
                        {
                            TimeSpan elapsed = nowUtc - lastChangeUtcByFolder[entry.FolderPath];
                            if (elapsed >= _folderInactivityInterval)
                            {
                                if (!_repeatFolderInactivityNotifications)
                                {
                                    if (inactivityPeriodsByFolder[entry.FolderPath] == 0)
                                    {
                                        inactivityPeriodsByFolder[entry.FolderPath] = 1;
                                        OnFolderWatchEventDetected(new FolderWatchEventArgs(
                                            entry.FolderPath,
                                            string.Empty,
                                            FolderWatchEventType.Inactive,
                                            elapsed));
                                    }
                                }
                                else
                                {
                                    int periods = (int)(elapsed.TotalMilliseconds / _folderInactivityInterval.TotalMilliseconds);
                                    if (periods > inactivityPeriodsByFolder[entry.FolderPath])
                                    {
                                        inactivityPeriodsByFolder[entry.FolderPath] = periods;
                                        OnFolderWatchEventDetected(new FolderWatchEventArgs(
                                            entry.FolderPath,
                                            string.Empty,
                                            FolderWatchEventType.Inactive,
                                            elapsed));
                                    }
                                }
                            }
                        }

                        lastSnapshotsByFolder[entry.FolderPath] = currentSnapshot;
                    }

                    await Task.Delay(_folderPollingInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when watcher is stopped.
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Warning: Folder watch error - {ex.Message}");
            }
        }

        private static Dictionary<string, FileSnapshotInfo> CaptureFolderSnapshot(string folderPath)
        {
            var snapshot = new Dictionary<string, FileSnapshotInfo>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(folderPath))
            {
                return snapshot;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

            foreach (string filePath in Directory.EnumerateFiles(folderPath, "*", options))
            {
                try
                {
                    var info = new FileInfo(filePath);
                    snapshot[filePath] = new FileSnapshotInfo
                    {
                        LastWriteUtc = info.LastWriteTimeUtc,
                        Length = info.Exists ? info.Length : 0
                    };
                }
                catch
                {
                    // Ignore files that become inaccessible during snapshot.
                }
            }

            return snapshot;
        }

        private static bool AreSnapshotsEqual(FileSnapshotInfo previous, FileSnapshotInfo current)
        {
            return previous.LastWriteUtc == current.LastWriteUtc && previous.Length == current.Length;
        }

        private static string GetRelativePathSafe(string basePath, string fullPath)
        {
            try
            {
                return Path.GetRelativePath(basePath, fullPath);
            }
            catch
            {
                return fullPath;
            }
        }

        private static bool MinimizeToTray(string appName)
        {
            try
            {
                string toolPath = Path.Combine(AppContext.BaseDirectory, "minimize-to-tray.exe");
                if (!File.Exists(toolPath))
                {
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = $"/NONOTIFY \"{appName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using Process? commandProcess = Process.Start(psi);
                return commandProcess != null;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeProcessName(string value)
        {
            string trimmed = value.Trim();
            return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^4]
                : trimmed;
        }

        private void OnAppLifecycleEventDetected(AppLifecycleEventArgs args)
        {
            AppLifecycleEventDetected?.Invoke(this, args);
        }

        private void OnInactivityDetected(InactivityEventArgs args)
        {
            InactivityDetected?.Invoke(this, args);
        }

        private void OnFolderWatchEventDetected(FolderWatchEventArgs args)
        {
            FolderWatchEventDetected?.Invoke(this, args);
        }

        #region Native Methods
        private const int HWND_BROADCAST = 0xFFFF;
        private const int WM_SYSCOMMAND = 0x0112;

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(int hWnd, int hMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool LockWorkStation();

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        #endregion
    }
}
