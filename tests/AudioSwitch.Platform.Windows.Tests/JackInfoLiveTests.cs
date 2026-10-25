using System.Text.Json;
using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;

namespace AudioSwitch.Platform.Windows.Tests;

public sealed class JackInfoLiveTests
{
    [LiveFact]
    public void JackConnectionTypesMatchProbeAndAtLeastOneEndpointHasAJackDescriptor()
    {
        var expected = SnapshotJson.Read(File.ReadAllText(EndpointSourceLiveTestsPath.ProbeSnapshot));
        var source = new WindowsJackInfoSource();
        var actual = expected.Endpoints.SelectMany(endpoint => source.GetEndpointJackInfo(endpoint.EndpointId)).ToArray();

        var expectedRows = expected.Jacks
            .Select(jack => (jack.EndpointId, ConnectionType: GetInt(jack.Jack, "connection_type")))
            .OrderBy(row => row.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ConnectionType)
            .ToArray();
        var actualRows = actual
            .Select(jack => (jack.EndpointId, ConnectionType: GetInt(jack.Jack, "connection_type")))
            .OrderBy(row => row.EndpointId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ConnectionType)
            .ToArray();

        Assert.Equal(expectedRows, actualRows);
        Assert.Contains(actual, jack => jack.Jack.ContainsKey("connection_type"));
    }

    private static int? GetInt(IReadOnlyDictionary<string, JsonElement> values, string key) =>
        values.TryGetValue(key, out var value) && value.TryGetInt32(out var result) ? result : null;
}
