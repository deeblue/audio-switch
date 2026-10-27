using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

/// <summary>Visible when the value is false.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value is not null.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The icon glyph of a device-tree row.</summary>
public sealed class DeviceNodeGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is DeviceNodeViewModel node ? Glyphs.ForNode(node) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A width minus the amount in the parameter, used to cap text width.</summary>
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

/// <summary>The brush key of a port's calibration state.</summary>
public sealed class PortStateBrushKeyConverter : IValueConverter
{
    public static PortStateBrushKeyConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PortCalibrationState state ? PortShapes.BrushKeyFor(state) : PortShapes.NeutralBrushKey;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
