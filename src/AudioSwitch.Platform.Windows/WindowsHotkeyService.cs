using System.Runtime.InteropServices;
using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows;

public sealed class WindowsHotkeyService : IHotkeyService
{
    private const uint ModNoRepeat = 0x4000;
    private readonly HashSet<int> _registered = [];
    private IntPtr _targetWindow;
    private bool _disposed;

    public HotkeyRegistrationResult Register(IntPtr targetWindow, string? playbackHotkey, string? recordingHotkey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unregister();
        _targetWindow = targetWindow;
        var bindings = new[] { (Id: 0x4151, Value: playbackHotkey), (Id: 0x4152, Value: recordingHotkey) };
        foreach (var (id, value) in bindings)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!HotkeyBindingParser.TryParse(value, out var binding)) { Unregister(); return new(false, $"無法辨識快捷鍵：{value}"); }
            if (!RegisterHotKey(targetWindow, id, (uint)binding.Modifiers | ModNoRepeat, binding.VirtualKey))
            {
                var error = Marshal.GetLastWin32Error();
                Unregister();
                return new(false, $"快捷鍵 {value} 無法註冊（錯誤碼 {error}，可能已被其他程式使用）。");
            }
            _registered.Add(id);
        }
        return new(true, null);
    }

    public void Unregister() { foreach (var id in _registered) UnregisterHotKey(_targetWindow, id); _registered.Clear(); _targetWindow = IntPtr.Zero; }
    public void Dispose() { if (_disposed) return; Unregister(); _disposed = true; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
