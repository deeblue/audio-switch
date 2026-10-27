using System.Text.Json;
using AudioSwitch.Core;

namespace AudioSwitch.Core.Tests;

public sealed class DockCalibrationTests
{
    private const string GpuAdapter = @"\\?\PCI#VEN_8086&DEV_7D51#GPU";

    [Fact]
    public void DockCalibrationRecordsItsUsbPathAndTheScreensOnTheHostGpu()
    {
        var ports = UsbCPort();
        var dockHub = new DeviceNode { InstanceId = @"USB\VID_03F0&PID_3BAF\DOCK", ParentId = @"USB\ROOT_HUB30\1", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)"] };
        var usbDisplay = new DeviceNode { InstanceId = @"USB\VID_34C7&PID_2114\ADAPTER", ParentId = dockHub.InstanceId, LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)#USB(4)"] };
        var after = new Snapshot
        {
            Devnodes = [dockHub, usbDisplay],
            DisplayPaths = [GpuPath("hdmi-screen", 8261, 0), GpuPath("dp-screen", 12613, 1), UsbAdapterPath(usbDisplay.InstanceId)]
        };

        var result = AttachmentDetector.Calibrate("usb-c-rear", new Snapshot(), after, ports);

        Assert.Equal("calibrated", result.Status);
        Assert.Equal(["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)"], result.LocationPaths);
        Assert.Equal(2, result.DisplayConnectors!.Count);
        var port = ports.FindInterface("usb-c-rear")!;
        Assert.Equal([8261, 12613], port.DisplayConnectors.Select(connector => connector.TargetId!.Value));
        Assert.All(port.DisplayConnectors, connector => Assert.Equal(GpuAdapter, connector.AdapterDevicePath));
    }

    [Fact]
    public void ScreenAudioOnACalibratedDockIsPlacedUnderItsHostPort()
    {
        var ports = UsbCPort();
        var dockHub = new DeviceNode { InstanceId = @"USB\VID_03F0&PID_3BAF\DOCK", ParentId = @"USB\ROOT_HUB30\1", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)"] };
        var screen = GpuPath("hdmi-screen", 8261, 0);
        screen.EdidManufacturerId = 25434;
        screen.EdidProductCodeId = 33579;
        AttachmentDetector.Calibrate("usb-c-rear", new Snapshot(), new Snapshot { Devnodes = [dockHub], DisplayPaths = [screen] }, ports);
        var endpoint = new AudioEndpoint { EndpointId = "screen-audio", Flow = "render", State = "active", FriendlyName = "VP2770 SERIES", DevnodeId = @"HDAUDIO\FUNC_01\1" };
        var sink = new Dictionary<string, JsonElement> { ["manufacturer_id"] = Json("25434"), ["product_id"] = Json("33579") };
        var snapshot = new Snapshot
        {
            Endpoints = [endpoint],
            Devnodes = [new DeviceNode { InstanceId = endpoint.DevnodeId }, dockHub],
            Jacks = [new JackRecord { EndpointId = endpoint.EndpointId, Sink = sink }],
            DisplayPaths = [screen]
        };

        var tree = TopologyBuilder.Build(snapshot, ports);

        var placed = Assert.Single(tree.Endpoints);
        Assert.Equal("B", placed.Category);
        Assert.Equal("usb-c-rear", placed.InterfaceId);
    }

    [Fact]
    public void ScreenOnAUsbDisplayAdapterWithoutAudioIsPlacedThroughTheDockUsbPath()
    {
        var ports = UsbCPort();
        var dockHub = new DeviceNode { InstanceId = @"USB\VID_03F0&PID_3BAF\DOCK", ParentId = @"USB\ROOT_HUB30\1", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)"] };
        var usbDisplay = new DeviceNode { InstanceId = @"USB\VID_34C7&PID_2114\ADAPTER", ParentId = dockHub.InstanceId, LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)#USB(4)"] };
        var after = new Snapshot { Devnodes = [dockHub, usbDisplay], DisplayPaths = [UsbAdapterPath(usbDisplay.InstanceId)] };
        AttachmentDetector.Calibrate("usb-c-rear", new Snapshot(), after, ports);
        Assert.Empty(ports.FindInterface("usb-c-rear")!.DisplayConnectors);

        var tree = TopologyBuilder.Build(after, ports);

        Assert.Equal("usb-c-rear", Assert.Single(tree.DisplayGroups).InterfaceId);
    }

    [Fact]
    public void DockOnACalibratedPortIsOneNodeHoldingItsScreensAndUsbDevices()
    {
        const string usb2Path = "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)";
        const string usb3Path = "PCIROOT(0)#PCI(0D00)#USBROOT(0)#USB(2)";
        var ports = PortTable.Read($$"""
            {"schema_version":1,"interfaces":[{"id":"usb-c-rear","name":"左側 USB-C（後）","type":"usb-c",
             "usb_location_path_prefixes":["{{usb2Path}}","{{usb3Path}}"],
             "display_connectors":[{"adapter_device_path":"{{GpuAdapter.Replace(@"\", @"\\", StringComparison.Ordinal)}}","target_id":8261,"connector_instance":0,"output_technology":10}]}]}
            """);
        var laptopPanel = new DisplayPath { PathId = "panel", SourceName = @"\\.\DISPLAY1", Primary = true, SourcePosition = Position(0, 0) };
        var screen = GpuPath("dock-screen", 8261, 0);
        screen.TargetName = "VP2770 SERIES";
        screen.SourceName = @"\\.\DISPLAY2";
        screen.SourcePosition = Position(1920, 0);
        screen.EdidManufacturerId = 25434;
        screen.EdidProductCodeId = 33579;
        var screenAudio = new AudioEndpoint { EndpointId = "screen-audio", Flow = "render", State = "active", FriendlyName = "VP2770 SERIES", DevnodeId = @"HDAUDIO\FUNC_01\1" };
        var speaker = new AudioEndpoint { EndpointId = "dock-speaker", Flow = "render", State = "active", FriendlyName = "Speakers", DevnodeId = @"SWD\MMDEVAPI\SPEAKER" };
        var snapshot = new Snapshot
        {
            Endpoints = [screenAudio, speaker],
            Devnodes =
            [
                new DeviceNode { InstanceId = screenAudio.DevnodeId },
                new DeviceNode { InstanceId = @"USB\ROOT_HUB30\XHCI" },
                new DeviceNode { InstanceId = @"USB\ROOT_HUB30\TXHC" },
                new DeviceNode { InstanceId = @"USB\VID_03F0&PID_3BAF\DOCK", ParentId = @"USB\ROOT_HUB30\XHCI", LocationPaths = [usb2Path] },
                new DeviceNode { InstanceId = @"USB\VID_8087&PID_0B40\TB", ParentId = @"USB\ROOT_HUB30\TXHC", LocationPaths = [usb3Path] },
                new DeviceNode { InstanceId = @"USB\VID_03F0&PID_4BAF\SS", ParentId = @"USB\VID_8087&PID_0B40\TB", LocationPaths = [usb3Path + "#USB(1)"] },
                new DeviceNode { InstanceId = @"USB\VID_03F0&PID_1BAF\INNER", ParentId = @"USB\VID_03F0&PID_3BAF\DOCK", LocationPaths = [usb2Path + "#USB(3)"] },
                new DeviceNode { InstanceId = @"USB\VID_303A&PID_2986\SPK", ParentId = @"USB\VID_03F0&PID_1BAF\INNER", Name = "speaker", LocationPaths = [usb2Path + "#USB(3)#USB(4)"] },
                new DeviceNode { InstanceId = speaker.DevnodeId, ParentId = @"USB\VID_303A&PID_2986\SPK" }
            ],
            Jacks = [new JackRecord { EndpointId = screenAudio.EndpointId, Sink = new() { ["manufacturer_id"] = Json("25434"), ["product_id"] = Json("33579") } }],
            DisplayPaths = [laptopPanel, screen]
        };

        var tree = TopologyBuilder.Build(snapshot, ports);

        var dock = Assert.Single(Assert.Single(tree.Roots, root => root.Id == "usb-c-rear").Children);
        Assert.Equal("dock-hub", dock.Kind);
        Assert.Equal("Dock／Hub", dock.Name);
        Assert.Equal("VID_03F0&PID_3BAF+VID_8087&PID_0B40", dock.DockKey);
        var screenNode = Assert.Single(dock.Children, child => child.Kind == "display");
        Assert.Equal("VP2770 SERIES（螢幕 2，右）", screenNode.Name);
        Assert.Equal("screen-audio", Assert.Single(screenNode.Children).EndpointId);
        var device = Assert.Single(dock.Children, child => child.Kind == "device");
        Assert.Equal("dock-speaker", Assert.Single(device.Children).EndpointId);
    }

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_7D51\GPU", 5, 1, "hdmi")]
    [InlineData(@"PCI\VEN_8086&DEV_7D51\GPU", 10, 1, null)]
    [InlineData(@"USB\VID_34C7&PID_2114\ADAPTER", 5, 1, null)]
    [InlineData(@"PCI\VEN_8086&DEV_7D51\GPU", 5, 2, null)]
    public void OnlyHdmiOnTheComputersGpuIsMatchedToTheSingleHdmiPortWithoutCalibration(string adapter, long technology, int hdmiPorts, string? expected)
    {
        var interfaces = string.Join(",", Enumerable.Range(0, hdmiPorts).Select(index => $$"""{"id":"{{(index == 0 ? "hdmi" : "hdmi-2")}}","name":"HDMI","type":"hdmi"}"""));
        var ports = PortTable.Read($$"""{"schema_version":1,"interfaces":[{{interfaces}}]}""");
        var screen = new DisplayPath { PathId = "screen", TargetName = "HDMI", AdapterInstanceId = adapter, OutputTechnology = technology, EdidManufacturerId = 1, EdidProductCodeId = 2 };
        var audio = new AudioEndpoint { EndpointId = "screen-audio", Flow = "render", State = "active", DevnodeId = @"HDAUDIO\FUNC_01\1" };
        var snapshot = new Snapshot
        {
            Endpoints = [audio],
            Devnodes = [new DeviceNode { InstanceId = audio.DevnodeId }],
            Jacks = [new JackRecord { EndpointId = audio.EndpointId, Sink = new() { ["manufacturer_id"] = Json("1"), ["product_id"] = Json("2") } }],
            DisplayPaths = [screen]
        };

        var tree = TopologyBuilder.Build(snapshot, ports);

        Assert.Equal(expected, Assert.Single(tree.DisplayGroups).InterfaceId);
        Assert.Equal(expected, Assert.Single(tree.Endpoints).InterfaceId);
    }

    [Fact]
    public void TwoUncalibratedHdmiScreensOnTheGpuLeaveTheHdmiPortUndecided()
    {
        var ports = PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"hdmi","name":"HDMI","type":"hdmi"}]}""");
        DisplayPath Hdmi(string id) => new() { PathId = id, AdapterInstanceId = @"PCI\VEN_8086&DEV_7D51\GPU", OutputTechnology = 5 };

        var tree = TopologyBuilder.Build(new Snapshot { DisplayPaths = [Hdmi("a"), Hdmi("b")] }, ports);

        Assert.All(tree.DisplayGroups, group => Assert.Null(group.InterfaceId));
    }

    [Fact]
    public void UsbAPortsNeverRecordScreens()
    {
        var ports = PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"usb-a","name":"USB-A","type":"usb-a"}]}""");
        var hub = new DeviceNode { InstanceId = @"USB\VID_1234&PID_5678\A", ParentId = @"USB\ROOT_HUB30\1", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(6)"] };

        var withUsb = AttachmentDetector.Calibrate("usb-a", new Snapshot(), new Snapshot { Devnodes = [hub], DisplayPaths = [GpuPath("screen", 4145, 0)] }, ports);
        var screenOnly = AttachmentDetector.Calibrate("usb-a", new Snapshot(), new Snapshot { DisplayPaths = [GpuPath("screen", 4145, 0)] }, ports);

        Assert.Equal("calibrated", withUsb.Status);
        Assert.Equal("no_candidate", screenOnly.Status);
        Assert.Empty(ports.FindInterface("usb-a")!.DisplayConnectors);
    }

    [Fact]
    public void ScreenAtThePrimaryPositionHasNoDirection()
    {
        var primary = new DisplayPath { PathId = "primary", SourceName = @"\\.\DISPLAY1", Primary = true, SourcePosition = Position(0, 0) };
        var copy = GpuPath("copy", 8261, 0);
        copy.TargetName = "Copy";
        copy.SourceName = @"\\.\DISPLAY2";
        copy.SourcePosition = Position(0, 0);
        var ports = PortTable.Read($$"""
            {"schema_version":1,"interfaces":[{"id":"usb-c-rear","name":"USB-C","type":"usb-c",
             "display_connectors":[{"adapter_device_path":"{{GpuAdapter.Replace(@"\", @"\\", StringComparison.Ordinal)}}","target_id":8261,"connector_instance":0,"output_technology":10}]}]}
            """);

        var tree = TopologyBuilder.Build(new Snapshot { DisplayPaths = [primary, copy] }, ports);

        Assert.Equal("Copy（螢幕 2）", Assert.Single(Assert.Single(tree.Roots).Children).Name);
    }

    [Fact]
    public void ScreensAreNumberedFromLeftToRightThenTopToBottom()
    {
        var numbers = ScreenNumbering.Number([(@"\\.\DISPLAY3", 1920, 0), (@"\\.\DISPLAY1", 0, 0), (@"\\.\DISPLAY69", 1920, -440), (@"\\.\DISPLAY2", -2560, 0)]);

        Assert.Equal(1, numbers[@"\\.\DISPLAY2"]);
        Assert.Equal(2, numbers[@"\\.\DISPLAY1"]);
        Assert.Equal(3, numbers[@"\\.\DISPLAY69"]);
        Assert.Equal(4, numbers[@"\\.\DISPLAY3"]);
    }

    [Fact]
    public void SeveralScreensWithoutUsbChangesCalibrateAUsbCPortButNotAnHdmiPort()
    {
        var after = new Snapshot { DisplayPaths = [GpuPath("first", 8261, 0), GpuPath("second", 12613, 1)] };
        var usbC = UsbCPort();
        var hdmi = PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"hdmi","name":"HDMI","type":"hdmi"}]}""");

        Assert.Equal("calibrated", AttachmentDetector.Calibrate("usb-c-rear", new Snapshot(), after, usbC).Status);
        Assert.Equal(2, usbC.FindInterface("usb-c-rear")!.DisplayConnectors.Count);
        Assert.Equal("ambiguous", AttachmentDetector.Calibrate("hdmi", new Snapshot(), after, hdmi).Status);
        Assert.Empty(hdmi.FindInterface("hdmi")!.DisplayConnectors);
    }

    private static PortTable UsbCPort() =>
        PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"usb-c-rear","name":"左側 USB-C（後）","type":"usb-c"}]}""");

    private static DisplayPath GpuPath(string pathId, int targetId, int connectorInstance) => new()
    {
        PathId = pathId,
        AdapterName = GpuAdapter,
        AdapterInstanceId = @"PCI\VEN_8086&DEV_7D51\GPU",
        ConnectorInstance = connectorInstance,
        OutputTechnology = 10,
        Properties = { ["target_id"] = Json(targetId.ToString(System.Globalization.CultureInfo.InvariantCulture)) }
    };

    private static DisplayPath UsbAdapterPath(string adapterInstanceId) => new()
    {
        PathId = "usb-screen",
        AdapterName = @"\\?\USB#VID_34C7&PID_2114#ADAPTER",
        AdapterInstanceId = adapterInstanceId,
        ConnectorInstance = 0,
        OutputTechnology = 5,
        Properties = { ["target_id"] = Json("256") }
    };

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static Dictionary<string, JsonElement> Position(int x, int y) => new()
    {
        ["x"] = Json(x.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["y"] = Json(y.ToString(System.Globalization.CultureInfo.InvariantCulture))
    };
}
