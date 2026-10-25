using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

public static class HotkeyGestureFormatter
{
    private static readonly HashSet<string> ModifierKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "LeftCtrl", "RightCtrl", "LeftControl", "RightControl", "LeftAlt", "RightAlt",
        "LeftShift", "RightShift", "LWin", "RWin", "LeftWindows", "RightWindows"
    };

    public static string? Format(HotkeyModifiers modifiers, string actualKey)
    {
        ArgumentNullException.ThrowIfNull(actualKey);
        if (ModifierKeys.Contains(actualKey)) return null;
        if (actualKey.Equals("Escape", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (modifiers == HotkeyModifiers.None || (modifiers & ~(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) != 0)
            return null;

        var tokens = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) tokens.Add("Win");
        if (modifiers.HasFlag(HotkeyModifiers.Control)) tokens.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) tokens.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) tokens.Add("Shift");
        tokens.Add(actualKey);
        var formatted = string.Join('+', tokens);
        return HotkeyBindingParser.TryParse(formatted, out _) ? formatted : null;
    }
}
