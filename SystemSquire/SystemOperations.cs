using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Security.Principal;

namespace SystemSquire
{
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

        public event EventHandler<string>? StatusChanged;

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

        public bool SetEthernetWakeOnLanEnabled(bool enabled, string adapterName = "Ethernet")
        {
            try
            {
                string targetState = enabled ? "Enabled" : "Disabled";
                string escapedAdapterName = EscapeForSingleQuotedPowerShellString(adapterName);
                string command =
                    $"Set-NetAdapterPowerManagement -Name '{escapedAdapterName}' -WakeOnMagicPacket {targetState} -ErrorAction Stop; " +
                    $"(Get-NetAdapterPowerManagement -Name '{escapedAdapterName}' -ErrorAction Stop).WakeOnMagicPacket";

                if (!TryRunPowerShellCommand(command, out string output, out string errorMessage))
                {
                    OnStatusChanged($"Error: {errorMessage}");
                    return false;
                }

                string resultingState = output.Trim();
                bool success = string.Equals(resultingState, targetState, StringComparison.OrdinalIgnoreCase);
                if (success)
                {
                    OnStatusChanged($"Wake-on-LAN for {adapterName} set to {targetState}");
                    return true;
                }

                OnStatusChanged($"Warning: Unable to confirm Wake-on-LAN state for {adapterName}. Current value: {resultingState}");
                return false;
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Error: {ex.Message}");
                return false;
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

            if (!IsRunningAsAdministrator())
            {
                errorMessage = "Wake-on-LAN operations require running System Squire as administrator.";
                return false;
            }

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

        private static string EscapeForSingleQuotedPowerShellString(string value)
        {
            return value.Replace("'", "''");
        }

        private static bool IsRunningAsAdministrator()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
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

        private static bool MinimizeToTray(string appName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "minimize-to-tray",
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

        #region Native Methods
        private const int HWND_BROADCAST = 0xFFFF;
        private const int WM_SYSCOMMAND = 0x0112;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(int hWnd, int hMsg, IntPtr wParam, IntPtr lParam);
        #endregion
    }
}
