namespace AudioSwitch.Presentation.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void PresentationAssemblyCanBeLoaded()
    {
        Assert.Equal("AudioSwitch.Presentation", typeof(AudioSwitch.Presentation.QuickSwitchPanelViewModel).Assembly.GetName().Name);
    }
}
