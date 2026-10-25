namespace AudioSwitch.Core.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void CoreAssemblyCanBeLoaded()
    {
        Assert.Equal("AudioSwitch.Core", typeof(AudioSwitch.Core.Class1).Assembly.GetName().Name);
    }
}
