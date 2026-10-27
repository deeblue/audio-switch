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

    /// <summary>A screen's name with its number and its position relative to the primary screen, for example "VP2770（螢幕 2，右）".</summary>
    internal static string Label(DisplayPath path, IReadOnlyList<DisplayPath> allPaths, IReadOnlyDictionary<string, int> numbers)
    {
        var details = new List<string>();
        if (path.SourceName is not null && numbers.TryGetValue(path.SourceName, out var number)) details.Add(Loc.F("螢幕 {0}", number));
        var primary = allPaths.FirstOrDefault(item => item.Primary == true);
        if (path.Primary == true)
        {
            details.Add(Loc.T("主螢幕"));
        }
        else if (primary?.SourcePosition is not null && path.SourcePosition is not null)
        {
            var x = JsonValue.Integer(path.SourcePosition, "x") ?? 0;
            var y = JsonValue.Integer(path.SourcePosition, "y") ?? 0;
            var primaryX = JsonValue.Integer(primary.SourcePosition, "x") ?? 0;
            var primaryY = JsonValue.Integer(primary.SourcePosition, "y") ?? 0;
            // A screen at the primary's own position (for example a duplicate) has no direction.
            if (x != primaryX || y != primaryY) details.Add(Loc.T(x < primaryX ? "左" : x > primaryX ? "右" : y < primaryY ? "上" : "下"));
        }
        var name = path.TargetName ?? Loc.T("顯示器");
        return details.Count == 0 ? name : Loc.F("{0}（{1}）", name, string.Join(Loc.T("，"), details));
    }
}
