using System.Drawing;
using System.Windows.Forms;
using AudioSwitch.Presentation;

namespace AudioSwitch.App;

public sealed class WindowsTrayAnchor : ITrayAnchor
{
    public PopupBounds PlacePopup(double width, double height)
    {
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        var left = Math.Clamp(cursor.X - (int)width + 16, area.Left, Math.Max(area.Left, area.Right - (int)width));
        var top = cursor.Y - (int)height - 12;
        if (top < area.Top) top = cursor.Y + 12;
        top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - (int)height));
        return new PopupBounds(left, top, width, height);
    }
}
