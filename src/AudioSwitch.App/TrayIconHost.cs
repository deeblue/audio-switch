using System.Drawing;
using WinForms = System.Windows.Forms;

namespace AudioSwitch.App;

public sealed class TrayIconHost : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;

    public WinForms.ContextMenuStrip Menu { get; }

    public event EventHandler? ExitRequested;

    public TrayIconHost()
    {
        Menu = new WinForms.ContextMenuStrip();
        Menu.Items.Add("結束", null, RequestExit);
        _notifyIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = Menu,
            Icon = SystemIcons.Application,
            Text = "AudioSwitch",
            Visible = true
        };
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Menu.Dispose();
    }

    private void RequestExit(object? sender, EventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
