using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

public partial class HotkeySettingsPageView : System.Windows.Controls.UserControl
{
    public event EventHandler? ApplyRequested;

    public HotkeySettingsPageView() => InitializeComponent();

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Windows;
        if (HotkeyGestureFormatter.Format(modifiers, key.ToString()) is not { } binding) return;
        if (sender == PlaybackHotkeyBox && DataContext is HotkeySettingsViewModel model)
            model.PlaybackHotkey = binding;
        else if (sender == RecordingHotkeyBox && DataContext is HotkeySettingsViewModel recordingModel)
            recordingModel.RecordingHotkey = binding;
    }

    private void ClearPlayback_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is HotkeySettingsViewModel model) model.PlaybackHotkey = null;
    }

    private void ClearRecording_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is HotkeySettingsViewModel model) model.RecordingHotkey = null;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyRequested?.Invoke(this, EventArgs.Empty);
}
