using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SystemSquire
{
    /// <summary>
    /// Handles system operations (shutdown, monitor control)
    /// </summary>
    public class SystemOperations
    {
        private bool _shutdownActive = false;
        private bool _cooldownActive = false;
        private CancellationTokenSource? _shutdownCts;
        private readonly string _dummyPath;

        public event EventHandler<string>? StatusChanged;

        public SystemOperations()
        {
            // Look for Dummy.exe in the same directory as the application
            _dummyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dummy.exe");
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
                OnStatusChanged("Shutdown initiated - 10 seconds");
                
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

        public async void TriggerBlackout()
        {
            try
            {
                OnStatusChanged("Blackout activated");
                
                // Launch Dummy.exe
                if (File.Exists(_dummyPath))
                {
                    Process.Start(_dummyPath);
                    
                    // Wait a moment for the window to appear
                    await Task.Delay(500);
                    
                    // Find and activate Dummy window
                    IntPtr hwnd = FindWindow(null, "Dummy");
                    if (hwnd != IntPtr.Zero)
                    {
                        SetForegroundWindow(hwnd);
                        ShowWindow(hwnd, SW_RESTORE);
                        await Task.Delay(300);
                    }
                }
                else
                {
                    OnStatusChanged($"Warning: Dummy.exe not found at {_dummyPath}");
                }
                
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

        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        #region Native Methods
        private const int HWND_BROADCAST = 0xFFFF;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(int hWnd, int hMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        #endregion
    }
}
