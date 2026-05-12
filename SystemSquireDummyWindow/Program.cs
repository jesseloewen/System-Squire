using System;
using System.Drawing;
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
    }
}
