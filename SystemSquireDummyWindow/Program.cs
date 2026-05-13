using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SystemSquireDummyWindow;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DummyWindowForm());
    }
}

internal sealed class DummyWindowForm : Form
{
    private const int FocusRetryAttempts = 20;
    private const int FocusRetryIntervalMs = 100;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const int SW_RESTORE = 9;

    private readonly Timer _focusRetryTimer;
    private int _remainingFocusRetries = FocusRetryAttempts;

    public DummyWindowForm()
    {
        Text = "System Squire Dummy Window";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        TopMost = true;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 120);
        BackColor = Color.FromArgb(25, 30, 40);

        var label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Text = "Dummy blackout window is active."
        };

        Controls.Add(label);

        _focusRetryTimer = new Timer { Interval = FocusRetryIntervalMs };
        _focusRetryTimer.Tick += FocusRetryTimer_Tick;

        Shown += (_, _) =>
        {
            _remainingFocusRetries = FocusRetryAttempts;
            EnsureForeground();
            _focusRetryTimer.Start();
        };
    }

    private void FocusRetryTimer_Tick(object? sender, EventArgs e)
    {
        if (EnsureForeground() || --_remainingFocusRetries <= 0)
        {
            _focusRetryTimer.Stop();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _focusRetryTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool EnsureForeground()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        ShowWindowAsync(Handle, SW_RESTORE);
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        BringToFront();
        Activate();
        SetForegroundWindow(Handle);

        return GetForegroundWindow() == Handle;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
}
