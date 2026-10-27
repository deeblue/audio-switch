namespace AudioSwitch.App.Tests;

public class TrayIconHostTests
{
    // Registers the pack:// scheme, which a running WPF Application otherwise provides.
    static TrayIconHostTests() => _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

    [Fact]
    public void ApplicationIconContainsAllRequiredSizes()
    {
        var iconPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AudioSwitch.App", "Assets", "AudioSwitch.ico"));
        Assert.True(File.Exists(iconPath), $"Expected application icon at {iconPath}");

        var bytes = File.ReadAllBytes(iconPath);
        Assert.Equal((ushort)1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        var dimensions = Enumerable.Range(0, count)
            .Select(index => bytes[6 + index * 16])
            .ToArray();

        Assert.Equal(new byte[] { 16, 20, 24, 32, 40, 48, 64, 0 }, dimensions);
    }

    [Theory]
    [InlineData(96, 16)]
    [InlineData(144, 24)]
    [InlineData(192, 32)]
    public void TrayIconUsesDpiAppropriateFrame(int dpi, int expectedSize) =>
        Assert.Equal(expectedSize, typeof(TrayIconHost).Assembly.GetType("AudioSwitch.App.AppIconProvider")!
            .GetMethod("GetTrayIconSize", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [dpi]));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TrayIconGlyphContrastsWithTaskbar(bool taskbarUsesLightTheme, bool expectLightGlyph)
    {
        using var icon = AppIconProvider.LoadTrayIcon(taskbarUsesLightTheme, 192);
        using var bitmap = icon.ToBitmap();
        var glyph = Enumerable.Range(0, bitmap.Width * bitmap.Height)
            .Select(index => bitmap.GetPixel(index % bitmap.Width, index / bitmap.Width))
            .Where(pixel => pixel.A > 128)
            .ToArray();

        Assert.NotEmpty(glyph);
        Assert.Equal(expectLightGlyph, glyph.Average(pixel => pixel.GetBrightness()) > 0.5);
    }

    [Theory]
    [InlineData(System.Windows.Forms.MouseButtons.Left, 1)]
    [InlineData(System.Windows.Forms.MouseButtons.Right, 1)]
    [InlineData(System.Windows.Forms.MouseButtons.Middle, 0)]
    public void LeftOrRightClickTogglesThePanel(System.Windows.Forms.MouseButtons button, int expected)
    {
        using var tray = new TrayIconHost();
        var requested = 0;
        tray.TogglePanelRequested += (_, _) => requested++;

        typeof(TrayIconHost).GetMethod("OnMouseClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(tray, [null, new System.Windows.Forms.MouseEventArgs(button, 1, 0, 0, 0)]);

        Assert.Equal(expected, requested);
    }
}
