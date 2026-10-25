using System.Drawing;
using System.Windows.Forms;
using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.App;

public sealed class WindowsTrayAnchor : ITrayAnchor
{
    private readonly IMonitorDpiSource _dpiSource;

    public WindowsTrayAnchor(IMonitorDpiSource dpiSource) => _dpiSource = dpiSource;

    public PopupBounds PlacePopup(double width, double height)
    {
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        var dpi = _dpiSource.GetDpiForPoint(cursor.X, cursor.Y);
        var bounds = PopupPlacement.PlaceNearCursor(
            new PhysicalRect(area.Left, area.Top, area.Width, area.Height),
            new PhysicalPoint(cursor.X, cursor.Y),
            new DpiScale(dpi.X / 96d, dpi.Y / 96d), width, height);
        return new PopupBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }
}
