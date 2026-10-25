using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class HotkeyGestureFormatterTests
{
    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, "F1", "Ctrl+Alt+F1")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift, "D1", "Ctrl+Shift+D1")]
    [InlineData(HotkeyModifiers.Windows | HotkeyModifiers.Alt, "A", "Win+Alt+A")]
    public void FormatsSupportedModifierAndKeyCombinations(HotkeyModifiers modifiers, string key, string expected)
    {
        Assert.Equal(expected, HotkeyGestureFormatter.Format(modifiers, key));
    }

    [Theory]
    [InlineData("LeftCtrl")]
    [InlineData("RightControl")]
    [InlineData("LeftShift")]
    [InlineData("RightAlt")]
    [InlineData("LWin")]
    [InlineData("RWin")]
    public void ModifierOnlyInputDoesNotProduceABinding(string key)
    {
        Assert.Null(HotkeyGestureFormatter.Format(HotkeyModifiers.Control, key));
    }

    [Fact]
    public void EscapeClearsTheBinding()
    {
        Assert.Equal(string.Empty, HotkeyGestureFormatter.Format(HotkeyModifiers.Control | HotkeyModifiers.Alt, "Escape"));
    }

    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, "F1")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift, "D1")]
    [InlineData(HotkeyModifiers.Windows | HotkeyModifiers.Alt, "A")]
    public void FormattedBindingsAreAcceptedByTheSharedParser(HotkeyModifiers modifiers, string key)
    {
        var binding = HotkeyGestureFormatter.Format(modifiers, key);

        Assert.NotNull(binding);
        Assert.True(HotkeyBindingParser.TryParse(binding, out _));
    }

    [Theory]
    [InlineData("Return", "Ctrl+Enter", 0x0D)]
    [InlineData("Next", "Ctrl+PageDown", 0x22)]
    [InlineData("Prior", "Ctrl+PageUp", 0x21)]
    [InlineData("NumPad1", "Ctrl+N1", 0x61)]
    public void NormalizesWpfKeyNamesToTheCorrectVirtualKey(string key, string expected, uint expectedVirtualKey)
    {
        var formatted = HotkeyGestureFormatter.Format(HotkeyModifiers.Control, key);

        Assert.Equal(expected, formatted);
        Assert.True(HotkeyBindingParser.TryParse(formatted, out var binding));
        Assert.Equal(expectedVirtualKey, binding.VirtualKey);
    }
}
