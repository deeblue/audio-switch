using System.Text.Json;
using AudioSwitch.Core;

namespace AudioSwitch.Core.Tests;

public sealed class TopologyGoldenTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void SnapshotClassificationAndGroupingMatchProbeGoldens()
    {
        var golden = ReadGolden();
        var portTable = LoadPorts();

        foreach (var expected in golden.Snapshots)
        {
            var snapshot = ReadSnapshot(expected.Snapshot);
            var tree = TopologyBuilder.Build(snapshot, portTable);
            var actual = tree.Endpoints.Select(endpoint => new EndpointGolden(
                endpoint.EndpointId,
                endpoint.Category,
                endpoint.InterfaceId,
                endpoint.GroupKey)).ToArray();
            var actualDisplays = tree.DisplayGroups.Select(display => new DisplayGolden(
                display.PathId,
                display.InterfaceId,
                display.GroupKey)).ToArray();

            Assert.Equal(expected.Endpoints, actual);
            Assert.Equal(expected.DisplayGroups, actualDisplays);
        }
    }

    [Fact]
    public void AdjacentPortMovesMatchCalibrationGoldens()
    {
        var golden = ReadGolden();
        foreach (var expected in golden.Calibrations)
        {
            var result = AttachmentDetector.Calibrate(
                expected.InterfaceId, ReadSnapshot(expected.Before), ReadSnapshot(expected.After), LoadPorts());

            Assert.Equal(expected.Result.Status, result.Status);
            Assert.Equal(expected.Result.InterfaceId, result.InterfaceId);
            Assert.Equal(expected.Result.Transports, result.Transports);
            Assert.Equal(expected.Result.LocationPaths, result.LocationPaths);
        }
    }

    [Fact]
    public void OldFinalSnapshotCalibratesOnlyTheMovedUsbDeviceAndExcludesRootComplexGpu()
    {
        var result = AttachmentDetector.Calibrate(
            "usb-a-left", ReadSnapshot("final-after"), ReadSnapshot("port-left-usba"), LoadPorts());

        Assert.Equal("calibrated", result.Status);
        Assert.Single(result.Attachments);
        Assert.StartsWith("USB\\", result.Attachments[0].InstanceId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SameNameEndpointsRemainSeparateAndUncalibratedUsbHasFriendlyRoot()
    {
        var sameName = new Snapshot
        {
            Endpoints =
            [
                new AudioEndpoint { EndpointId = "ep-one", State = "active", Flow = "render", FriendlyName = "Realtek", DevnodeId = @"SWD\MMDEVAPI\ep-one" },
                new AudioEndpoint { EndpointId = "ep-two", State = "active", Flow = "render", FriendlyName = "Realtek", DevnodeId = @"SWD\MMDEVAPI\ep-two" }
            ],
            Devnodes =
            [
                new DeviceNode { InstanceId = @"SWD\MMDEVAPI\ep-one", ParentId = @"USB\VID_1&PID_1\A" },
                new DeviceNode { InstanceId = @"SWD\MMDEVAPI\ep-two", ParentId = @"USB\VID_1&PID_1\B" },
                new DeviceNode { InstanceId = @"USB\VID_1&PID_1\A", LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(2)"] },
                new DeviceNode { InstanceId = @"USB\VID_1&PID_1\B", LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(3)"] }
            ]
        };

        var tree = TopologyBuilder.Build(sameName, PortTable.Empty());

        Assert.Equal(["ep-one", "ep-two"], tree.Endpoints.Select(endpoint => endpoint.EndpointId));
        Assert.Equal(["USB（未設定接口）"], tree.Roots.Select(root => root.Name));
        Assert.DoesNotContain(tree.Roots.SelectMany(root => root.Children), node => node.Name.Contains("USB(", StringComparison.Ordinal));
    }

    private GoldenDocument ReadGolden() => JsonSerializer.Deserialize<GoldenDocument>(
        File.ReadAllText(Fixture("topology-golden.json")), JsonOptions)!;

    private PortTable LoadPorts() => PortTable.Read(File.ReadAllText(Fixture("ports.json")));

    private Snapshot ReadSnapshot(string name) => SnapshotJson.Read(File.ReadAllText(Fixture(name + ".json")));

    private string Fixture(string name) => Path.Combine(_fixtures, name);

    private sealed record GoldenDocument(GoldenSnapshot[] Snapshots, GoldenCalibration[] Calibrations);
    private sealed record GoldenSnapshot(string Snapshot, EndpointGolden[] Endpoints, DisplayGolden[] DisplayGroups);
    private sealed record GoldenCalibration(string Before, string After, string InterfaceId, GoldenResult Result);
    private sealed record GoldenResult(string Status, string? InterfaceId, string[] Transports, string[] LocationPaths);
    private sealed record EndpointGolden(string EndpointId, string Category, string? InterfaceId, string? GroupKey);
    private sealed record DisplayGolden(string PathId, string? InterfaceId, string? GroupKey);
}
