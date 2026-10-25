namespace AudioSwitch.App.Tests;

public class TrayIconHostTests
{
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
