using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;

namespace AudioSwitch.Platform.Windows.Tests;

public sealed class DeviceTreeLiveTests
{
    [LiveFact]
    public void EndpointAncestorLocationPathsMatchProbeCapture()
    {
        var expected = SnapshotJson.Read(File.ReadAllText(EndpointSourceLiveTestsPath.ProbeSnapshot));
        var source = new WindowsDeviceTreeSource();
        var expectedNodes = expected.Devnodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
        var activeExpected = expected.Endpoints.Where(ProbeEndpointState.IsActive);

        foreach (var endpoint in activeExpected)
        {
            var actualChain = source.GetAncestors(endpoint.DevnodeId!);
            var expectedChain = BuildExpectedChain(endpoint.DevnodeId!, expectedNodes);
            Assert.Equal(expectedChain.SelectMany(node => node.LocationPaths).Order(StringComparer.OrdinalIgnoreCase),
                actualChain.SelectMany(node => node.LocationPaths).Order(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static IReadOnlyList<DeviceNode> BuildExpectedChain(string start, IReadOnlyDictionary<string, DeviceNode> nodes)
    {
        var result = new List<DeviceNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? current = start;
        while (current is not null && seen.Add(current) && nodes.TryGetValue(current, out var node))
        {
            result.Add(node);
            current = node.ParentId;
        }
        return result;
    }
}

internal static class EndpointSourceLiveTestsPath
{
    public static string ProbeSnapshot
    {
        get
        {
            for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "probes", "out", "live-compare.json");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("Missing probes/out/live-compare.json");
        }
    }
}
