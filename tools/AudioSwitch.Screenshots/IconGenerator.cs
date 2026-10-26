using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

/// <summary>Renders the SVG icon sources (a small subset: rect, path, linearGradient) into multi-size .ico files.</summary>
static class IconGenerator
{
    public static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];
    const int SmallSizeLimit = 24;

    public static void WriteIcons(string assetsDirectory)
    {
        WriteIcon(Path.Combine(assetsDirectory, "AudioSwitch.ico"),
            size => Render(Path.Combine(assetsDirectory, size <= SmallSizeLimit ? "AudioSwitch.Small.svg" : "AudioSwitch.svg"), size, null));
        WriteIcon(Path.Combine(assetsDirectory, "AudioSwitch.Tray.Dark.ico"),
            size => Render(Path.Combine(assetsDirectory, size <= SmallSizeLimit ? "AudioSwitch.Tray.Small.svg" : "AudioSwitch.Tray.svg"), size, Color.FromRgb(0x1A, 0x1A, 0x1A)));
        WriteIcon(Path.Combine(assetsDirectory, "AudioSwitch.Tray.Light.ico"),
            size => Render(Path.Combine(assetsDirectory, size <= SmallSizeLimit ? "AudioSwitch.Tray.Small.svg" : "AudioSwitch.Tray.svg"), size, Colors.White));
    }

    /// <summary>Shows every frame of the shipped .ico files on light and dark backgrounds, enlarged with nearest-neighbour so pixels stay visible.</summary>
    public static void WriteOverview(string assetsDirectory, string outputPath)
    {
        var rows = new (string Caption, string File, Color Background)[]
        {
            ("App · 淺色底", "AudioSwitch.ico", Colors.White),
            ("App · 深色底", "AudioSwitch.ico", Color.FromRgb(32, 32, 32)),
            ("系統匣 · 淺色工作列", "AudioSwitch.Tray.Dark.ico", Color.FromRgb(238, 238, 238)),
            ("系統匣 · 深色工作列", "AudioSwitch.Tray.Light.ico", Color.FromRgb(28, 28, 28))
        };
        const int cell = 112;
        var grid = new Grid { Background = new SolidColorBrush(Color.FromRgb(200, 200, 200)) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        foreach (var _ in Sizes) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
        for (var row = 0; row < rows.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });
            var (caption, file, background) = rows[row];
            var foreground = new SolidColorBrush(background.R + background.G + background.B > 380 ? Color.FromRgb(20, 20, 20) : Color.FromRgb(240, 240, 240));
            var label = new Border { Background = new SolidColorBrush(background), Child = new TextBlock { Text = caption, Foreground = foreground, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center } };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            using var stream = File.OpenRead(Path.Combine(assetsDirectory, file));
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            for (var column = 0; column < Sizes.Length; column++)
            {
                var size = Sizes[column];
                var frame = decoder.Frames.First(item => item.PixelWidth == size);
                var display = Math.Min(size * 3, 80);
                var image = new Image { Source = frame, Width = display, Height = display };
                RenderOptions.SetBitmapScalingMode(image, size <= 32 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock { Text = $"{size}px", Foreground = foreground, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
                stack.Children.Add(image);
                var box = new Border { Background = new SolidColorBrush(background), Child = stack, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 1, 0) };
                Grid.SetRow(box, row);
                Grid.SetColumn(box, column + 1);
                grid.Children.Add(box);
            }
        }
        grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        grid.Arrange(new Rect(grid.DesiredSize));
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)grid.ActualWidth, (int)grid.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var output = File.Create(outputPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(output);
        Console.WriteLine(outputPath);
    }

    static void WriteIcon(string path, Func<int, BitmapSource> render)
    {
        var frames = Sizes.Select(size => (Size: size, Data: size >= 256 ? EncodePng(render(size)) : EncodeDib(render(size)))).ToArray();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Length);
        var offset = 6 + 16 * frames.Length;
        foreach (var (size, data) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) writer.Write(data);
        Console.WriteLine(path);
    }

    static byte[] EncodePng(BitmapSource bitmap)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return stream.ToArray();
    }

    // 32-bit BGRA DIB with an all-zero AND mask; transparency comes from the alpha channel.
    static byte[] EncodeDib(BitmapSource bitmap)
    {
        var size = bitmap.PixelWidth;
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4];
        converted.CopyPixels(pixels, size * 4, 0);
        var maskStride = ((size + 31) / 32) * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixels.Length + maskStride * size);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        for (var row = size - 1; row >= 0; row--) writer.Write(pixels, row * size * 4, size * 4);
        writer.Write(new byte[maskStride * size]);
        return stream.ToArray();
    }

    static BitmapSource Render(string svgPath, int size, Color? currentColor)
    {
        var svg = XDocument.Load(svgPath).Root!;
        var ns = svg.Name.Namespace;
        var viewBox = svg.Attribute("viewBox")!.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
        var gradients = svg.Descendants(ns + "linearGradient").ToDictionary(element => element.Attribute("id")!.Value, element =>
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(Number(element.Attribute("x1")?.Value ?? "0"), Number(element.Attribute("y1")?.Value ?? "0")),
                EndPoint = new Point(Number(element.Attribute("x2")?.Value ?? "1"), Number(element.Attribute("y2")?.Value ?? "0"))
            };
            foreach (var stop in element.Elements(ns + "stop"))
                brush.GradientStops.Add(new GradientStop(ParseColor(stop.Attribute("stop-color")!.Value, currentColor), Number(stop.Attribute("offset")!.Value)));
            return (Brush)brush;
        });
        Brush? Paint(string? value)
        {
            if (value is null or "none") return null;
            if (value.StartsWith("url(#", StringComparison.Ordinal)) return gradients[value[5..^1]];
            return new SolidColorBrush(ParseColor(value, currentColor));
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(size / viewBox[2], size / viewBox[3]));
            context.PushTransform(new TranslateTransform(-viewBox[0], -viewBox[1]));
            foreach (var element in svg.Elements().Where(element => element.Name.LocalName is "rect" or "path"))
            {
                Geometry geometry = element.Name.LocalName == "rect"
                    ? new RectangleGeometry(new Rect(Number(element.Attribute("x")!.Value), Number(element.Attribute("y")!.Value),
                        Number(element.Attribute("width")!.Value), Number(element.Attribute("height")!.Value)),
                        Number(element.Attribute("rx")?.Value ?? "0"), Number(element.Attribute("rx")?.Value ?? "0"))
                    : Geometry.Parse(element.Attribute("d")!.Value);
                var stroke = Paint(element.Attribute("stroke")?.Value);
                Pen? pen = null;
                if (stroke is not null)
                {
                    pen = new Pen(stroke, Number(element.Attribute("stroke-width")?.Value ?? "1"));
                    if (element.Attribute("stroke-linecap")?.Value == "round") pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                    if (element.Attribute("stroke-linejoin")?.Value == "round") pen.LineJoin = PenLineJoin.Round;
                }
                context.DrawGeometry(Paint(element.Attribute("fill")?.Value ?? "#000000"), pen, geometry);
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    static Color ParseColor(string value, Color? currentColor) =>
        value == "currentColor" ? currentColor ?? Colors.Black : (Color)ColorConverter.ConvertFromString(value);

    static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
