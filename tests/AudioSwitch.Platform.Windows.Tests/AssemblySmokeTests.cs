namespace AudioSwitch.Platform.Windows.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void WindowsPlatformAssemblyCanBeLoaded()
    {
        Assert.Equal("AudioSwitch.Platform.Windows", typeof(AudioSwitch.Platform.Windows.WindowsAudioEndpointSource).Assembly.GetName().Name);
    }
}
