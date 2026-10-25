namespace AudioSwitch.Core;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed record HotkeyBinding(HotkeyModifiers Modifiers, uint VirtualKey);

public static class HotkeyBindingParser
{
    private const HotkeyModifiers AllModifiers = HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows;

    public static bool TryParse(string? value, out HotkeyBinding binding)
    {
        binding = new HotkeyBinding(HotkeyModifiers.None, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None
            };
            if (modifier == HotkeyModifiers.None || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }

        if (modifiers == HotkeyModifiers.None || (modifiers & ~AllModifiers) != 0 || !TryGetVirtualKey(parts[^1], out var virtualKey)) return false;
        binding = new HotkeyBinding(modifiers, virtualKey);
        return true;
    }

    private static bool TryGetVirtualKey(string value, out uint virtualKey)
    {
        virtualKey = 0;
        if (value.Length == 1 && char.IsAsciiLetter(value[0]))
        {
            virtualKey = char.ToUpperInvariant(value[0]);
            return true;
        }
        if (value.Length == 2 && (value[0] is 'D' or 'd' or 'N' or 'n') && char.IsAsciiDigit(value[1]))
        {
            virtualKey = value[1];
            return true;
        }
        if (value.StartsWith('F') && int.TryParse(value[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        virtualKey = value.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" => 0x1B,
            "BACK" or "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => 0
        };
        return virtualKey != 0;
    }
}
