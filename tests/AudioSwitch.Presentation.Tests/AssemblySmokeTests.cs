namespace AudioSwitch.Presentation.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void PresentationAssemblyCanBeLoaded()
    {
        Assert.Equal("AudioSwitch.Presentation", typeof(AudioSwitch.Presentation.Class1).Assembly.GetName().Name);
    }
}
