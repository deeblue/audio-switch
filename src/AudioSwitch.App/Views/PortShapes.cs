using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

/// <summary>Front-view outlines of physical connectors, drawn so each type keeps its recognisable shape.</summary>
public static class PortShapes
{
    public const string NeutralBrushKey = "TextFillColorSecondaryBrush";
    private const string HoleBrushKey = "SolidBackgroundFillColorBaseBrush";

    public static Size BaseSize(string type) => type switch
    {
        "usb-a" => new Size(26, 11),
        "usb-c" => new Size(22, 9),
        "hdmi" => new Size(26, 11),
        "displayport" => new Size(24, 11),
        "audio-jack" => new Size(12, 12),
        _ => new Size(22, 10)
    };

    public static string BrushKeyFor(PortCalibrationState state) => state switch
    {
        PortCalibrationState.Calibrated or PortCalibrationState.Automatic => "SystemFillColorSuccessBrush",
        PortCalibrationState.Partial => "SystemFillColorCautionBrush",
        _ => "ControlStrongFillColorDefaultBrush"
    };

    public static FrameworkElement Create(string type, string fillBrushKey, double scale = 1)
    {
        var size = BaseSize(type);
        var canvas = new Canvas { Width = size.Width, Height = size.Height, SnapsToDevicePixels = false };
        switch (type)
        {
            case "usb-a":
                Add(canvas, Rounded(0, 0, 26, 11, 1.5), fillBrushKey);
                Add(canvas, Rounded(2.5, 2.5, 21, 6, 0.5), HoleBrushKey);
                Add(canvas, Rounded(4.5, 2.5, 17, 2.5, 0), fillBrushKey);
                break;
            case "usb-c":
                Add(canvas, Rounded(0, 0, 22, 9, 4.5), fillBrushKey);
                Add(canvas, Rounded(2, 2, 18, 5, 2.5), HoleBrushKey);
                Add(canvas, Rounded(6, 4, 10, 1.2, 0.6), fillBrushKey);
                break;
            case "hdmi":
                Add(canvas, Polygon((0, 0), (26, 0), (26, 6.5), (22, 11), (4, 11), (0, 6.5)), fillBrushKey);
                Add(canvas, Polygon((2.5, 2.5), (23.5, 2.5), (23.5, 5.5), (20.5, 8.5), (5.5, 8.5), (2.5, 5.5)), HoleBrushKey);
                Add(canvas, Rounded(5.5, 4, 15, 1.5, 0), fillBrushKey);
                break;
            case "displayport":
                Add(canvas, Polygon((0, 0), (24, 0), (24, 11), (4.5, 11), (0, 6.5)), fillBrushKey);
                Add(canvas, Polygon((2.5, 2.5), (21.5, 2.5), (21.5, 8.5), (5.5, 8.5), (2.5, 5.5)), HoleBrushKey);
                break;
            case "audio-jack":
                Add(canvas, new EllipseGeometry(new Point(6, 6), 6, 6), fillBrushKey);
                Add(canvas, new EllipseGeometry(new Point(6, 6), 2.8, 2.8), HoleBrushKey);
                break;
            default:
                Add(canvas, Rounded(0, 0, 22, 10, 2), fillBrushKey);
                Add(canvas, Rounded(2.5, 2.5, 17, 5, 1), HoleBrushKey);
                break;
        }
        return new Viewbox
        {
            Width = size.Width * scale,
            Height = size.Height * scale,
            Stretch = Stretch.Uniform,
            Child = canvas
        };
    }

    private static void Add(Canvas canvas, Geometry geometry, string brushKey)
    {
        var path = new Path { Data = geometry };
        path.SetResourceReference(Shape.FillProperty, brushKey);
        canvas.Children.Add(path);
    }

    private static Geometry Rounded(double x, double y, double width, double height, double radius) =>
        new RectangleGeometry(new Rect(x, y, width, height), radius, radius);

    private static Geometry Polygon(params (double X, double Y)[] points)
    {
        var figure = new PathFigure { StartPoint = new Point(points[0].X, points[0].Y), IsClosed = true, IsFilled = true };
        foreach (var (x, y) in points.Skip(1)) figure.Segments.Add(new LineSegment(new Point(x, y), true));
        return new PathGeometry([figure]);
    }
}

/// <summary>A connector outline usable from XAML, e.g. as a tree icon for a port.</summary>
public sealed class PortGlyph : Decorator
{
    public static readonly DependencyProperty ConnectorTypeProperty = DependencyProperty.Register(nameof(ConnectorType),
        typeof(string), typeof(PortGlyph), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty FillBrushKeyProperty = DependencyProperty.Register(nameof(FillBrushKey),
        typeof(string), typeof(PortGlyph), new PropertyMetadata(PortShapes.NeutralBrushKey, OnChanged));
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(nameof(Scale),
        typeof(double), typeof(PortGlyph), new PropertyMetadata(0.8, OnChanged));

    public string? ConnectorType { get => (string?)GetValue(ConnectorTypeProperty); set => SetValue(ConnectorTypeProperty, value); }
    public string FillBrushKey { get => (string)GetValue(FillBrushKeyProperty); set => SetValue(FillBrushKeyProperty, value); }
    public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var glyph = (PortGlyph)d;
        glyph.Child = glyph.ConnectorType is { } type ? PortShapes.Create(type, glyph.FillBrushKey, glyph.Scale) : null;
        if (glyph.Child is FrameworkElement child)
        {
            child.HorizontalAlignment = HorizontalAlignment.Center;
            child.VerticalAlignment = VerticalAlignment.Center;
        }
    }
}
