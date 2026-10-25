using System.Runtime.InteropServices;
using AudioSwitch.Core;

namespace AudioSwitch.Platform.Windows;

public sealed class WindowsHotkeyService : IHotkeyService
{
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModWin = 0x0008, ModNoRepeat = 0x4000;
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
            if (!TryParse(value, out var modifiers, out var key)) { Unregister(); return new(false, $"無法辨識快捷鍵：{value}"); }
            if (!RegisterHotKey(targetWindow, id, modifiers | ModNoRepeat, key))
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

    private static bool TryParse(string text, out uint modifiers, out uint key)
    {
        modifiers = 0; key = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            var flag = part.ToLowerInvariant() switch { "ctrl" or "control" => ModControl, "alt" => ModAlt, "shift" => ModShift, "win" => ModWin, _ => 0u };
            if (flag == 0) return false;
            modifiers |= flag;
        }
        var last = parts[^1];
        if (last.Length == 1 && char.IsLetterOrDigit(last[0])) key = char.ToUpperInvariant(last[0]);
        else if (last.Length == 2 && (last[0] is 'D' or 'd') && last[1] is >= '0' and <= '9') key = last[1];
        else if (last.StartsWith('F') && int.TryParse(last[1..], out var f) && f is >= 1 and <= 24) key = (uint)(0x70 + f - 1);
        else key = last.ToUpperInvariant() switch { "SPACE" => 0x20u, "ENTER" => 0x0Du, "TAB" => 0x09u, "ESCAPE" => 0x1Bu, "BACK" or "BACKSPACE" => 0x08u, "DELETE" => 0x2Eu, "INSERT" => 0x2Du, "HOME" => 0x24u, "END" => 0x23u, "PAGEUP" => 0x21u, "PAGEDOWN" => 0x22u, "LEFT" => 0x25u, "UP" => 0x26u, "RIGHT" => 0x27u, "DOWN" => 0x28u, _ => 0u };
        return key != 0 && modifiers != 0;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
