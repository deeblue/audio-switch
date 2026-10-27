using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DeviceNodeGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is DeviceNodeViewModel node ? Glyphs.ForNode(node) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class Glyphs
{
    public const string Speaker = "";
    public const string Microphone = "";
    public const string Monitor = "";
    public const string Bluetooth = "";
    public const string UsbDevice = "";
    public const string Hub = "";
    public const string Laptop = "";
    public const string Unknown = "";
    public const string Virtual = "";
    public const string ChevronUp = "";
    public const string ChevronDown = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";

    public static string ForNode(DeviceNodeViewModel node) => node.Kind switch
    {
        DeviceNodeKind.Playback => Speaker,
        DeviceNodeKind.Recording => Microphone,
        DeviceNodeKind.Display => Monitor,
        DeviceNodeKind.DockHub => Hub,
        DeviceNodeKind.Device => UsbDevice,
        DeviceNodeKind.Category => node.CategoryId switch
        {
            "內建" => Laptop,
            "藍牙／無線" => Bluetooth,
            "虛擬" => Virtual,
            TopologyBuilder.UnconfiguredUsbRootId => UsbDevice,
            _ => Unknown
        },
        _ => string.Empty
    };
}

public sealed class SubtractConverter : IValueConverter
{
    public static SubtractConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double number && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            ? Math.Max(0, number - amount)
            : double.PositiveInfinity;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PortStateBrushKeyConverter : IValueConverter
{
    public static PortStateBrushKeyConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PortCalibrationState state ? PortShapes.BrushKeyFor(state) : PortShapes.NeutralBrushKey;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A colour swatch with its meaning, used for the port status legend.</summary>
public sealed class LegendItem : System.Windows.Controls.StackPanel
{
    private readonly System.Windows.Shapes.Ellipse _swatch = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Controls.TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };

    public LegendItem()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        _label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        _label.FontSize = 12;
        Children.Add(_swatch);
        Children.Add(_label);
    }

    private string _brushKey = PortShapes.NeutralBrushKey;

    public string BrushKey
    {
        get => _brushKey;
        set
        {
            _brushKey = value;
            _swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, value);
        }
    }
    public string Text { get => _label.Text; set => _label.Text = value; }

    /// <summary>Shows a filled badge with this glyph instead of a colour dot, matching the current-device badges on the drawing.</summary>
    public string? Glyph
    {
        get => (_badge?.Child as System.Windows.Controls.TextBlock)?.Text;
        set
        {
            if (_badge is null)
            {
                var icon = new System.Windows.Controls.TextBlock { FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                icon.SetResourceReference(System.Windows.Controls.TextBlock.FontFamilyProperty, "IconFont");
                icon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
                _badge = new System.Windows.Controls.Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = icon
                };
                _badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentFillColorDefaultBrush");
                Children.Remove(_swatch);
                Children.Insert(0, _badge);
            }
            ((System.Windows.Controls.TextBlock)_badge.Child).Text = value;
        }
    }

    private System.Windows.Controls.Border? _badge;
}
