using Microsoft.Win32;
using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows;

/// <summary>Starting with Windows through the current user's Run key; nothing is written outside HKCU.</summary>
public sealed class WindowsAutostartService : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AudioSwitch";

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException(Loc.T("無法開啟目前使用者的自動啟動設定。"));
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException(Loc.T("無法取得程式路徑。"));
            key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
