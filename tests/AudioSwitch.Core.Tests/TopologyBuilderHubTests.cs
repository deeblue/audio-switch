using AudioSwitch.Core;

namespace AudioSwitch.Core.Tests;

public sealed class TopologyBuilderHubTests
{
    [Fact]
    public void AudioBelowExternalHubAppearsUnderUnconfiguredDockHubNode()
    {
        var tree = TopologyBuilder.Build(CreateUsbAudioTree(["HUB", "SPEAKER"]), PortTable.Empty());

        var root = Assert.Single(tree.Roots);
        Assert.Equal("USB（未設定接口）", root.Id);
        var hub = Assert.Single(root.Children);
        Assert.Equal("Dock／Hub（未定義）", hub.Name);
        Assert.Equal("dock-hub", hub.Kind);
        Assert.Equal("USB 音訊裝置", Assert.Single(hub.Children).Name);
        Assert.Equal("usb-speaker", Assert.Single(Assert.Single(hub.Children).Children).EndpointId);
    }

    [Fact]
    public void HubsConnectedInSeriesProduceNestedDockHubNodes()
    {
        var tree = TopologyBuilder.Build(CreateUsbAudioTree(["DOCK", "HUB", "SPEAKER"]), PortTable.Empty());

        var firstHub = Assert.Single(Assert.Single(tree.Roots).Children);
        var secondHub = Assert.Single(firstHub.Children);
        Assert.Equal("Dock／Hub（未定義）", firstHub.Name);
        Assert.Equal("Dock／Hub（未定義）", secondHub.Name);
        Assert.Equal("USB 音訊裝置", Assert.Single(secondHub.Children).Name);
    }

    [Fact]
    public void DirectUsbAudioDeviceDoesNotGainDockHubNode()
    {
        var tree = TopologyBuilder.Build(CreateUsbAudioTree(["SPEAKER"]), PortTable.Empty());

        var device = Assert.Single(Assert.Single(tree.Roots).Children);
        Assert.Equal("USB 音訊裝置", device.Name);
        Assert.Equal("device", device.Kind);
        Assert.Equal("usb-speaker", Assert.Single(device.Children).EndpointId);
    }

    [Fact]
    public void UsbDisplayPathAndAudioEndpointShareTheirPhysicalDeviceNode()
    {
        var snapshot = CreateUsbAudioTree(["DISPLAY"]);
        var physicalId = snapshot.Devnodes.Single(node => node.InstanceId.StartsWith(@"USB\VID_DISPLAY\", StringComparison.OrdinalIgnoreCase)).InstanceId;
        snapshot.Devnodes.Add(new DeviceNode { InstanceId = physicalId + "&MI_00", ParentId = physicalId, Name = "USB Display Interface" });
        snapshot.DisplayPaths.Add(new DisplayPath { PathId = "display-1", TargetName = "USB Monitor", AdapterInstanceId = physicalId + "&MI_00" });

        var tree = TopologyBuilder.Build(snapshot, PortTable.Empty());

        var device = Assert.Single(Assert.Single(tree.Roots).Children);
        Assert.Equal(physicalId, device.GroupKey);
        Assert.Contains(device.Children, child => child.EndpointId == "usb-speaker");
        Assert.Contains(device.Children, child => child.Kind == "display" && child.Name == "USB Monitor");
    }

    private static Snapshot CreateUsbAudioTree(IReadOnlyList<string> downstreamIds)
    {
        var nodes = new List<DeviceNode>
        {
            new() { InstanceId = @"USB\ROOT_HUB30\ROOT" }
        };
        var parentId = @"USB\ROOT_HUB30\ROOT";
        for (var index = 0; index < downstreamIds.Count; index++)
        {
            var id = $@"USB\VID_{downstreamIds[index]}\{index}";
            var node = new DeviceNode
            {
                InstanceId = id,
                ParentId = parentId,
                Name = downstreamIds[index] == "SPEAKER" ? "USB 音訊裝置" : "USB Hub",
                LocationPaths = [$"PCIROOT(0)#USBROOT(0)#USB(3){string.Concat(Enumerable.Repeat("#USB(1)", index))}"]
            };
            nodes.Add(node);
            parentId = id;
        }
        var endpoint = new AudioEndpoint { EndpointId = "usb-speaker", Flow = "render", State = "active", DevnodeId = @"SWD\MMDEVAPI\usb-speaker" };
        nodes.Add(new DeviceNode { InstanceId = endpoint.DevnodeId!, ParentId = parentId });
        return new Snapshot { Endpoints = [endpoint], Devnodes = nodes };
    }
}
