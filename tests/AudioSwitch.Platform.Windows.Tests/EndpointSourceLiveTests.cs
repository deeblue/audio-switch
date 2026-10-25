using System.Text.Json;
using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;

namespace AudioSwitch.Platform.Windows.Tests;

public sealed class EndpointSourceLiveTests
{
    [LiveFact]
    public void ActiveEndpointIdsAndFlowsMatchProbeCapture()
    {
        var probePath = FindRepoFile(Path.Combine("probes", "out", "live-compare.json"));
        var expected = SnapshotJson.Read(File.ReadAllText(probePath));
        var activeExpected = expected.Endpoints.Where(ProbeEndpointState.IsActive).ToArray();
        using var source = new WindowsAudioEndpointSource();

        var actual = source.EnumerateActiveEndpoints();

        Assert.Equal(activeExpected.Select(endpoint => endpoint.EndpointId).Order(StringComparer.OrdinalIgnoreCase),
            actual.Select(endpoint => endpoint.EndpointId).Order(StringComparer.OrdinalIgnoreCase));
        foreach (var endpoint in activeExpected)
        {
            var found = actual.Single(item => item.EndpointId.Equals(endpoint.EndpointId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(endpoint.Flow, found.Flow);
            Assert.Equal(endpoint.Defaults, found.Defaults);
        }
    }

    [Fact]
    public void EndpointNotificationSubscriptionCanBeDisposed()
    {
        using var source = new WindowsAudioEndpointSource();
        var eventCount = 0;
        EventHandler<EndpointNotificationEventArgs> handler = (_, _) => eventCount++;
        source.EndpointChanged += handler;
        source.DefaultEndpointChanged += handler;
        source.EndpointChanged -= handler;
        source.DefaultEndpointChanged -= handler;
    }

    private static string FindRepoFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException($"Cannot find repository file {relativePath}");
    }
}

public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AUDIOSWITCH_LIVE") != "1")
        {
            Skip = "Set AUDIOSWITCH_LIVE=1 to run Windows live-device tests.";
        }
    }
}
