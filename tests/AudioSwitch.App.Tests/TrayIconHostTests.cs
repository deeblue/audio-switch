namespace AudioSwitch.App.Tests;

public class TrayIconHostTests
{
    [Fact]
    public void ExitMenuItemRequestsApplicationExit()
    {
        using var tray = new TrayIconHost();
        var requested = 0;
        tray.ExitRequested += (_, _) => requested++;

        var exitItem = Assert.Single(tray.Menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>());
        Assert.Equal("結束", exitItem.Text);
        exitItem.PerformClick();

        Assert.Equal(1, requested);
    }
}
