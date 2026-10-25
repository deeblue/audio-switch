using System.Drawing;
using WinForms = System.Windows.Forms;

namespace AudioSwitch.App;

public sealed class TrayIconHost : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;

    public WinForms.ContextMenuStrip Menu { get; }

    public event EventHandler? ExitRequested;
    public event EventHandler? TogglePanelRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? IdentifyScreensRequested;

    public void ShowNotice(string title, string message, WinForms.ToolTipIcon icon) =>
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);

    public TrayIconHost()
    {
        Menu = new WinForms.ContextMenuStrip();
        Menu.Items.Add("設定", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        Menu.Items.Add("識別螢幕", null, (_, _) => IdentifyScreensRequested?.Invoke(this, EventArgs.Empty));
        Menu.Items.Add("結束", null, RequestExit);
        _notifyIcon = new WinForms.NotifyIcon
        {
            ContextMenuStrip = Menu,
            Icon = SystemIcons.Application,
            Text = "AudioSwitch",
            Visible = true
        };
        _notifyIcon.MouseClick += OnMouseClick;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Dispose();
        Menu.Dispose();
    }

    private void RequestExit(object? sender, EventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
            TogglePanelRequested?.Invoke(this, EventArgs.Empty);
    }
}
