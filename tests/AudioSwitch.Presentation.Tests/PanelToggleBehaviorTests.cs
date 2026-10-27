using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class PanelToggleBehaviorTests
{
    [Fact]
    public void TrayClickAfterFocusLossDoesNotReopenPanel()
    {
        var behavior = new PanelToggleBehavior(TimeSpan.FromMilliseconds(250));
        var now = DateTimeOffset.UtcNow;
        behavior.RecordFocusLoss(now);

        Assert.Equal(PanelToggleAction.Ignore, behavior.OnTrayClick(false, now.AddMilliseconds(100)));
    }

    [Fact]
    public void TrayClickAfterSuppressionWindowOpensPanel()
    {
        var behavior = new PanelToggleBehavior(TimeSpan.FromMilliseconds(250));
        var now = DateTimeOffset.UtcNow;
        behavior.RecordFocusLoss(now);

        Assert.Equal(PanelToggleAction.Open, behavior.OnTrayClick(false, now.AddMilliseconds(251)));
    }

    [Fact]
    public void OnlyTheFirstClickOfARapidBurstActs()
    {
        var behavior = new PanelToggleBehavior(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500));
        var start = DateTimeOffset.UtcNow;

        Assert.Equal(PanelToggleAction.Open, behavior.OnTrayClick(false, start));
        // Each click in the burst comes 300 ms after the previous one, so the burst keeps being ignored.
        Assert.Equal(PanelToggleAction.Ignore, behavior.OnTrayClick(true, start.AddMilliseconds(300)));
        Assert.Equal(PanelToggleAction.Ignore, behavior.OnTrayClick(true, start.AddMilliseconds(600)));
        Assert.Equal(PanelToggleAction.Ignore, behavior.OnTrayClick(true, start.AddMilliseconds(900)));

        Assert.Equal(PanelToggleAction.Close, behavior.OnTrayClick(true, start.AddMilliseconds(1500)));
    }

    [Fact]
    public void TrayClickWhilePanelIsStillOpenClosesIt()
    {
        var behavior = new PanelToggleBehavior(TimeSpan.FromMilliseconds(250));

        Assert.Equal(PanelToggleAction.Close, behavior.OnTrayClick(true, DateTimeOffset.UtcNow));
    }
}
