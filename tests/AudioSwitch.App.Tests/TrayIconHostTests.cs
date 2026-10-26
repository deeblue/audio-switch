namespace AudioSwitch.App.Tests;

public class TrayIconHostTests
{
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

    [Fact]
    public void ExitMenuItemRequestsApplicationExit()
    {
        using var tray = new TrayIconHost();
        var requested = 0;
        tray.ExitRequested += (_, _) => requested++;

        Assert.Equal(new[] { "設定", "識別螢幕", "結束" }, tray.Menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Select(item => item.Text));
        var exitItem = tray.Menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Last();
        Assert.Equal("結束", exitItem.Text);
        exitItem.PerformClick();

        Assert.Equal(1, requested);
    }

    [Fact]
    public void SettingsAndIdentifyMenuItemsRaiseTheirRequests()
    {
        using var tray = new TrayIconHost();
        var settings = 0;
        var identify = 0;
        tray.SettingsRequested += (_, _) => settings++;
        tray.IdentifyScreensRequested += (_, _) => identify++;
        var items = tray.Menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().ToArray();

        items[0].PerformClick();
        items[1].PerformClick();

        Assert.Equal(1, settings);
        Assert.Equal(1, identify);
    }
}
