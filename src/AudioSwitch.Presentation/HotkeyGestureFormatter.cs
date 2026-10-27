using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

/// <summary>Turns a key press in the shortcut box into shortcut text such as "Ctrl+Alt+F1".</summary>
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
        tokens.Add(NormalizeKeyName(actualKey));
        var formatted = string.Join('+', tokens);
        return HotkeyBindingParser.TryParse(formatted, out _) ? formatted : null;
    }

    private static string NormalizeKeyName(string key) => key switch
    {
        "Return" => "Enter",
        "Next" => "PageDown",
        "Prior" => "PageUp",
        "NumPad0" => "N0",
        "NumPad1" => "N1",
        "NumPad2" => "N2",
        "NumPad3" => "N3",
        "NumPad4" => "N4",
        "NumPad5" => "N5",
        "NumPad6" => "N6",
        "NumPad7" => "N7",
        "NumPad8" => "N8",
        "NumPad9" => "N9",
        _ => key
    };
}
