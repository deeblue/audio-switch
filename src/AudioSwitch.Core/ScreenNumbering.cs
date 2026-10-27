namespace AudioSwitch.Core;

/// <summary>
/// Numbers screens from left to right, then top to bottom, by their desktop position. The device tree and the
/// identify overlay both use this order, so "螢幕 2" in the tree is the screen that shows a large 2.
/// </summary>
public static class ScreenNumbering
{
    /// <param name="screens">Each screen's GDI device name (for example <c>\\.\DISPLAY2</c>) and the top-left corner of its desktop area.</param>
    public static IReadOnlyDictionary<string, int> Number(IEnumerable<(string DeviceName, int X, int Y)> screens) =>
        screens
            .GroupBy(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(screen => screen.X)
            .ThenBy(screen => screen.Y)
            .ThenBy(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select((screen, index) => (screen.DeviceName, Number: index + 1))
            .ToDictionary(item => item.DeviceName, item => item.Number, StringComparer.OrdinalIgnoreCase);
}
