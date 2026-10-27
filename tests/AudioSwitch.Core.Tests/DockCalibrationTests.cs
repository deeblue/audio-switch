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
}
