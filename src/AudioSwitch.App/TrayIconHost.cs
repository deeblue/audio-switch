using System.Drawing;
using WinForms = System.Windows.Forms;

namespace AudioSwitch.App;

/// <summary>The tray icon: either mouse button opens the panel, and the icon follows the taskbar theme and DPI.</summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private Icon? _currentIcon;

    public event EventHandler? TogglePanelRequested;

    public void ShowNotice(string title, string message, WinForms.ToolTipIcon icon) =>
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);

    public TrayIconHost()
    {
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = $"AudioSwitch {AppVersion.Version}",
            Visible = true
        };
        UpdateIcon();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _notifyIcon.MouseClick += OnMouseClick;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= OnMouseClick;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }

    // Either button opens the panel; settings and exit live in the panel itself.
    private void OnMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button is WinForms.MouseButtons.Left or WinForms.MouseButtons.Right)
            TogglePanelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General || e.Category == Microsoft.Win32.UserPreferenceCategory.Color)
            UpdateIcon();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => UpdateIcon();

    private void UpdateIcon()
    {
        var next = AppIconProvider.LoadTrayIcon(AppIconProvider.IsTaskbarLightTheme(), AppIconProvider.GetCurrentDpi());
        _notifyIcon.Icon = next;
        _currentIcon?.Dispose();
        _currentIcon = next;
    }
}
