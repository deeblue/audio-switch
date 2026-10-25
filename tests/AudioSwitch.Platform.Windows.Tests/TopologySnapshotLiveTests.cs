using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;

namespace AudioSwitch.Platform.Windows.Tests;

public sealed class TopologySnapshotLiveTests
{
    [LiveFact]
    public void SnapshotContainsLiveEndpointsMachineAndTopologyData()
    {
        var expected = SnapshotJson.Read(File.ReadAllText(EndpointSourceLiveTestsPath.ProbeSnapshot));
        var activeExpected = expected.Endpoints.Where(ProbeEndpointState.IsActive).ToArray();
        using var endpointSource = new WindowsAudioEndpointSource();
        var actual = new WindowsAudioTopologySnapshotSource(endpoints: endpointSource).Capture();

        Assert.Equal(activeExpected.Select(item => item.EndpointId).Order(StringComparer.Ordinal),
            actual.Endpoints.Select(item => item.EndpointId).Order(StringComparer.Ordinal));
        Assert.Equal(expected.Machine.Manufacturer, actual.Machine.Manufacturer);
        Assert.Equal(expected.Machine.Model, actual.Machine.Model);
        Assert.Equal(expected.Machine.MtM, actual.Machine.MtM);
        Assert.Equal(expected.DisplayPaths.Select(item => item.PathId).Order(StringComparer.Ordinal),
            actual.DisplayPaths.Select(item => item.PathId).Order(StringComparer.Ordinal));

        foreach (var endpoint in actual.Endpoints.Where(item => item.DevnodeId is not null))
        {
            Assert.Contains(actual.Devnodes, node => node.InstanceId.Equals(endpoint.DevnodeId, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var jack in actual.Jacks)
        {
            Assert.Contains(actual.Endpoints, endpoint => endpoint.EndpointId == jack.EndpointId);
        }
    }
}
