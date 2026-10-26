using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

public sealed class PortDropEventArgs(string? newType, string? portId, PortEdge edge, int order) : EventArgs
{
    public string? NewType { get; } = newType;
    public string? PortId { get; } = portId;
    public PortEdge Edge { get; } = edge;
    public int Order { get; } = order;
}

/// <summary>Top-down laptop drawing with the user's ports on the chassis edges.</summary>
public sealed class LaptopDiagramView : UserControl
{
    public const string PortTypeFormat = "AudioSwitch.PortType";
    public const string PortIdFormat = "AudioSwitch.PortId";

    private const double CanvasWidth = 440;
    private const double CanvasHeight = 300;
    private static readonly Rect Body = new(70, 58, 300, 206);
    private const double EdgeInset = 26;

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items),
        typeof(IReadOnlyList<PortDiagramItem>), typeof(LaptopDiagramView), new PropertyMetadata(null, OnVisualPropertyChanged));
    public static readonly DependencyProperty SelectedIdProperty = DependencyProperty.Register(nameof(SelectedId),
        typeof(string), typeof(LaptopDiagramView), new PropertyMetadata(null, OnVisualPropertyChanged));
    public static readonly DependencyProperty HighlightedIdProperty = DependencyProperty.Register(nameof(HighlightedId),
        typeof(string), typeof(LaptopDiagramView), new PropertyMetadata(null, OnVisualPropertyChanged));
    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(nameof(IsInteractive),
        typeof(bool), typeof(LaptopDiagramView), new PropertyMetadata(true, OnVisualPropertyChanged));

    private readonly Canvas _canvas = new() { Width = CanvasWidth, Height = CanvasHeight, Background = Brushes.Transparent };
    private readonly Canvas _portLayer = new() { Width = CanvasWidth, Height = CanvasHeight };
    private readonly Rectangle _dropIndicator = new() { Visibility = Visibility.Collapsed, RadiusX = 2, RadiusY = 2, IsHitTestVisible = false };
    private Point? _pressPoint;
    private string? _pressedId;

    public LaptopDiagramView()
    {
        DrawLaptop();
        _canvas.Children.Add(_portLayer);
        _dropIndicator.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
        _canvas.Children.Add(_dropIndicator);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas };
        AllowDrop = true;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _dropIndicator.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
        AutomationProperties.SetName(this, "筆電接口示意圖");
    }

    public IReadOnlyList<PortDiagramItem>? Items { get => (IReadOnlyList<PortDiagramItem>?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public string? SelectedId { get => (string?)GetValue(SelectedIdProperty); set => SetValue(SelectedIdProperty, value); }
    public string? HighlightedId { get => (string?)GetValue(HighlightedIdProperty); set => SetValue(HighlightedIdProperty, value); }
    public bool IsInteractive { get => (bool)GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }

    public event EventHandler<string>? PortSelected;
    public event EventHandler<PortDropEventArgs>? PortDropped;

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LaptopDiagramView)d).RenderPorts();

    private void DrawLaptop()
    {
        // Screen lid seen edge-on behind the hinge, with the webcam dot in the middle.
        var lid = new Rectangle { Width = Body.Width - 8, Height = 12, RadiusX = 5, RadiusY = 5 };
        Place(lid, Body.Left + 4, Body.Top - 24, "ControlStrongFillColorDefaultBrush", null);
        var camera = new Ellipse { Width = 4, Height = 4 };
        Place(camera, Body.Left + Body.Width / 2 - 2, Body.Top - 20, "SolidBackgroundFillColorBaseBrush", null);

        var body = new Rectangle { Width = Body.Width, Height = Body.Height, RadiusX = 14, RadiusY = 14, StrokeThickness = 1.5 };
        Place(body, Body.Left, Body.Top, "SolidBackgroundFillColorTertiaryBrush", "ControlStrongStrokeColorDefaultBrush");
        var hinge = new Rectangle { Width = Body.Width - 40, Height = 4, RadiusX = 2, RadiusY = 2 };
        Place(hinge, Body.Left + 20, Body.Top + 8, "ControlStrokeColorSecondaryBrush", null);

        const double keyboardLeft = 96, keyboardTop = 80, keyboardWidth = 248;
        const double gap = 3;
        var rowHeights = new[] { 10d, 15, 15, 15, 15 };
        var top = keyboardTop;
        for (var row = 0; row < rowHeights.Length; row++)
        {
            const int columns = 14;
            var keyWidth = (keyboardWidth - gap * (columns - 1)) / columns;
            for (var column = 0; column < columns; column++)
                Key(keyboardLeft + column * (keyWidth + gap), top, keyWidth, rowHeights[row]);
            top += rowHeights[row] + gap;
        }
        // Bottom row: modifiers around a wide space bar.
        var unit = (keyboardWidth - gap * 9) / 14;
        var x = keyboardLeft;
        foreach (var width in new[] { 1d, 1, 1, 1.5, 6, 1.5, 1 })
        {
            Key(x, top, unit * width + (width > 1 ? gap * (Math.Ceiling(width) - 1) : 0), 15);
            x += unit * width + (width > 1 ? gap * (Math.Ceiling(width) - 1) : 0) + gap;
        }

        var touchpad = new Rectangle { Width = 108, Height = 58, RadiusX = 6, RadiusY = 6, StrokeThickness = 1 };
        Place(touchpad, Body.Left + (Body.Width - 108) / 2, Body.Bottom - 70, "ControlFillColorDefaultBrush", "ControlStrokeColorDefaultBrush");
        var lip = new Rectangle { Width = 44, Height = 3, RadiusX = 1.5, RadiusY = 1.5 };
        Place(lip, Body.Left + (Body.Width - 44) / 2, Body.Bottom - 6, "ControlStrokeColorSecondaryBrush", null);
    }

    private void Key(double left, double top, double width, double height)
    {
        var key = new Rectangle { Width = Math.Max(1, width), Height = height, RadiusX = 2, RadiusY = 2, StrokeThickness = 0.75 };
        Place(key, left, top, "ControlFillColorDefaultBrush", "ControlStrokeColorDefaultBrush");
    }

    private void Place(Shape shape, double left, double top, string fillKey, string? strokeKey)
    {
        shape.SetResourceReference(Shape.FillProperty, fillKey);
        if (strokeKey is not null) shape.SetResourceReference(Shape.StrokeProperty, strokeKey);
        shape.IsHitTestVisible = false;
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        _canvas.Children.Add(shape);
    }

    private void RenderPorts()
    {
        _portLayer.Children.Clear();
        if (Items is null) return;
        foreach (var group in Items.GroupBy(item => item.Edge))
        {
            var ports = group.OrderBy(item => item.Order).ToArray();
            var (step, scale) = Spacing(group.Key, ports.Length);
            var start = EdgeMid(group.Key) - step * (ports.Length - 1) / 2;
            for (var index = 0; index < ports.Length; index++)
                AddPort(ports[index], start + index * step, scale);
        }
    }

    private static (double Step, double Scale) Spacing(PortEdge edge, int count)
    {
        var span = EdgeLength(edge) - EdgeInset * 2;
        const double preferredStep = 40;
        var step = count <= 1 ? preferredStep : Math.Min(preferredStep, span / (count - 1));
        var scale = Math.Clamp((step - 6) / 26, 0.55, 1.3);
        return (step, scale);
    }

    private void AddPort(PortDiagramItem item, double along, double scale)
    {
        var vertical = item.Edge is PortEdge.Left or PortEdge.Right;
        var shape = PortShapes.Create(item.Type, PortShapes.BrushKeyFor(item.State), scale);
        if (vertical) shape.LayoutTransform = new RotateTransform(90);
        var emphasized = item.Id == SelectedId || item.Id == HighlightedId;
        var size = PortShapes.BaseSize(item.Type);
        var longSide = Math.Max(size.Width, size.Height) * scale + 8;
        var shortSide = Math.Min(size.Width, size.Height) * scale + 8;
        var target = new Border
        {
            Width = vertical ? shortSide : longSide,
            Height = vertical ? longSide : shortSide,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            Child = shape,
            Tag = item.Id,
            ToolTip = item.Description,
            Cursor = IsInteractive ? System.Windows.Input.Cursors.Hand : null
        };
        target.SetResourceReference(Border.BorderBrushProperty, emphasized ? "AccentFillColorDefaultBrush" : "SubtleFillColorTransparentBrush");
        if (emphasized) target.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        AutomationProperties.SetName(target, item.Description);
        ToolTipService.SetInitialShowDelay(target, 200);
        var center = EdgePoint(item.Edge, along);
        Canvas.SetLeft(target, center.X - target.Width / 2);
        Canvas.SetTop(target, center.Y - target.Height / 2);
        if (IsInteractive)
        {
            target.MouseLeftButtonDown += (_, e) =>
            {
                _pressPoint = e.GetPosition(_canvas);
                _pressedId = item.Id;
                e.Handled = true;
            };
            target.MouseMove += (_, e) =>
            {
                if (_pressedId != item.Id || _pressPoint is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
                var delta = e.GetPosition(_canvas) - start;
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _pressedId = null;
                _pressPoint = null;
                PortSelected?.Invoke(this, item.Id);
                DragDrop.DoDragDrop(target, new DataObject(PortIdFormat, item.Id), DragDropEffects.Move);
            };
            target.MouseLeftButtonUp += (_, e) =>
            {
                if (_pressedId == item.Id) PortSelected?.Invoke(this, item.Id);
                _pressedId = null;
                _pressPoint = null;
                e.Handled = true;
            };
        }
        _portLayer.Children.Add(target);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!IsInteractive || !(e.Data.GetDataPresent(PortTypeFormat) || e.Data.GetDataPresent(PortIdFormat)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var edge = NearestEdge(e.GetPosition(_canvas));
        var vertical = edge is PortEdge.Left or PortEdge.Right;
        var length = EdgeLength(edge) - EdgeInset * 2;
        _dropIndicator.Width = vertical ? 4 : length;
        _dropIndicator.Height = vertical ? length : 4;
        var mid = EdgePoint(edge, EdgeMid(edge));
        Canvas.SetLeft(_dropIndicator, mid.X - _dropIndicator.Width / 2);
        Canvas.SetTop(_dropIndicator, mid.Y - _dropIndicator.Height / 2);
        _dropIndicator.Opacity = 0.6;
        _dropIndicator.Visibility = Visibility.Visible;
        e.Effects = e.Data.GetDataPresent(PortIdFormat) ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dropIndicator.Visibility = Visibility.Collapsed;
        if (!IsInteractive) return;
        var movingId = e.Data.GetData(PortIdFormat) as string;
        var newType = e.Data.GetData(PortTypeFormat) as string;
        if (movingId is null && newType is null) return;
        var (edge, order) = DropTarget(e.GetPosition(_canvas), Items ?? [], movingId);
        PortDropped?.Invoke(this, new PortDropEventArgs(newType, movingId, edge, order));
        e.Handled = true;
    }

    /// <summary>Edge nearest to a canvas point, and the insertion index among the other ports drawn on that edge.</summary>
    internal static (PortEdge Edge, int Order) DropTarget(Point point, IReadOnlyList<PortDiagramItem> items, string? movingId)
    {
        var edge = NearestEdge(point);
        var along = Along(edge, point);
        var drawn = items.Where(item => item.Edge == edge).OrderBy(item => item.Order).ToArray();
        var (step, _) = Spacing(edge, drawn.Length);
        var start = EdgeMid(edge) - step * (drawn.Length - 1) / 2;
        var order = drawn.Select((item, index) => (item, Center: start + index * step))
            .Count(entry => entry.item.Id != movingId && entry.Center < along);
        return (edge, order);
    }

    /// <summary>Canvas position of the centre of a drawn port, for tests and drop previews.</summary>
    internal static Point PortCenter(IReadOnlyList<PortDiagramItem> items, PortDiagramItem item)
    {
        var drawn = items.Where(other => other.Edge == item.Edge).OrderBy(other => other.Order).ToArray();
        var (step, _) = Spacing(item.Edge, drawn.Length);
        var start = EdgeMid(item.Edge) - step * (drawn.Length - 1) / 2;
        return EdgePoint(item.Edge, start + Array.IndexOf(drawn, item) * step);
    }

    private static PortEdge NearestEdge(Point point)
    {
        var horizontalInside = Math.Clamp(point.X, Body.Left, Body.Right);
        var verticalInside = Math.Clamp(point.Y, Body.Top, Body.Bottom);
        var distances = new (PortEdge Edge, double Distance)[]
        {
            (PortEdge.Left, Distance(point, new Point(Body.Left, verticalInside))),
            (PortEdge.Right, Distance(point, new Point(Body.Right, verticalInside))),
            (PortEdge.Rear, Distance(point, new Point(horizontalInside, Body.Top))),
            (PortEdge.Front, Distance(point, new Point(horizontalInside, Body.Bottom)))
        };
        return distances.MinBy(item => item.Distance).Edge;
    }

    private static double Distance(Point first, Point second) => (first - second).Length;
    private static double EdgeLength(PortEdge edge) => edge is PortEdge.Left or PortEdge.Right ? Body.Height : Body.Width;
    private static double EdgeMid(PortEdge edge) => EdgeLength(edge) / 2;
    private static double Along(PortEdge edge, Point point) => edge is PortEdge.Left or PortEdge.Right ? point.Y - Body.Top : point.X - Body.Left;

    private static Point EdgePoint(PortEdge edge, double along) => edge switch
    {
        PortEdge.Left => new Point(Body.Left, Body.Top + along),
        PortEdge.Right => new Point(Body.Right, Body.Top + along),
        PortEdge.Rear => new Point(Body.Left + along, Body.Top),
        _ => new Point(Body.Left + along, Body.Bottom)
    };
}
