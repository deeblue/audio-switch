namespace AudioSwitch.Presentation;

public readonly record struct PhysicalPoint(int X, int Y);
public readonly record struct PhysicalRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}
public readonly record struct DpiScale(double X, double Y);
public readonly record struct DipBounds(double Left, double Top, double Width, double Height);

public static class PopupPlacement
{
    public static DipBounds PlaceNearCursor(PhysicalRect workArea, PhysicalPoint cursor, DpiScale dpi,
        double widthDip, double heightDip)
    {
        Validate(workArea, dpi, widthDip, heightDip);
        var widthPixels = widthDip * dpi.X;
        var heightPixels = heightDip * dpi.Y;
        var left = Math.Clamp(cursor.X - widthPixels + 16 * dpi.X, workArea.Left, workArea.Right - widthPixels);
        var top = cursor.Y - heightPixels - 12 * dpi.Y;
        if (top < workArea.Top) top = cursor.Y + 12 * dpi.Y;
        top = Math.Clamp(top, workArea.Top, workArea.Bottom - heightPixels);
        return new DipBounds(left / dpi.X, top / dpi.Y, widthDip, heightDip);
    }

    public static DipBounds ToDipBounds(PhysicalRect physicalBounds, DpiScale dpi)
    {
        if (dpi.X <= 0 || dpi.Y <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        return new DipBounds(physicalBounds.Left / dpi.X, physicalBounds.Top / dpi.Y,
            physicalBounds.Width / dpi.X, physicalBounds.Height / dpi.Y);
    }

    private static void Validate(PhysicalRect area, DpiScale dpi, double widthDip, double heightDip)
    {
        if (area.Width <= 0 || area.Height <= 0 || dpi.X <= 0 || dpi.Y <= 0 || widthDip <= 0 || heightDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(area), "工作區、DPI 與面板尺寸必須大於零。");
        if (widthDip * dpi.X > area.Width || heightDip * dpi.Y > area.Height)
            throw new ArgumentOutOfRangeException(nameof(widthDip), "面板尺寸不能大於螢幕工作區。");
    }
}
