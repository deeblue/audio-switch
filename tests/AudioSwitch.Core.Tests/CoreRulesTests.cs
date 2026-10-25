using AudioSwitch.Core;

namespace AudioSwitch.Core.Tests;

public sealed class CoreRulesTests
{
    private readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void TwoNewDevicesOnTheSameTransportAreAmbiguousAndDoNotChangePortTable()
    {
        var before = new Snapshot();
        var after = new Snapshot
        {
            Devnodes =
            [
                new DeviceNode { InstanceId = @"USB\VID_1&PID_1\A", LocationPaths = ["PCIROOT(0)#USBROOT(0)#HS01"] },
                new DeviceNode { InstanceId = @"USB\VID_2&PID_2\B", LocationPaths = ["PCIROOT(0)#USBROOT(0)#HS02"] }
            ]
        };
        var ports = PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"p1","name":"Port","type":"usb-c-tbt","usb_location_path_prefixes":[]}] }""");

        var result = AttachmentDetector.Calibrate("p1", before, after, ports);

        Assert.Equal("ambiguous", result.Status);
        Assert.Equal(2, result.CandidateIds.Count);
        Assert.Empty(ports.FindInterface("p1")!.UsbLocationPathPrefixes);
    }

    [Fact]
    public void CalibrationIgnoresPropertyChangesAndExcludesPciRootComplexGpu()
    {
        var rootBefore = new DeviceNode { InstanceId = @"USB\ROOT_HUB30\1", LocationPaths = ["PCIROOT(0)#USBROOT(0)"] };
        var rootAfter = new DeviceNode { InstanceId = rootBefore.InstanceId, LocationPaths = rootBefore.LocationPaths.ToList() };
        rootAfter.Properties["removal_policy"] = System.Text.Json.JsonDocument.Parse("1").RootElement.Clone();
        var usbBefore = new DeviceNode { InstanceId = @"USB\VID_1&PID_1\A", ParentId = rootBefore.InstanceId, LocationPaths = ["PCIROOT(0)#USBROOT(0)#HS01"] };
        var usbAfter = new DeviceNode { InstanceId = usbBefore.InstanceId, ParentId = rootBefore.InstanceId, LocationPaths = ["PCIROOT(0)#USBROOT(0)#HS02"] };
        var igpu = new DeviceNode { InstanceId = @"PCI\VEN_8086&DEV_7D51\GPU", LocationPaths = ["PCIROOT(0)#PCI(0200)"] };
        var before = new Snapshot { Devnodes = [rootBefore, usbBefore] };
        var after = new Snapshot { Devnodes = [rootAfter, usbAfter, igpu] };

        var attachments = AttachmentDetector.Detect(before, after);

        var attachment = Assert.Single(attachments);
        Assert.Equal(usbAfter.InstanceId, attachment.InstanceId);
        Assert.Equal(["USB 2"], AttachmentDetector.Calibrate("p1", before, after,
            PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"p1","name":"Port","type":"usb-a","usb_location_path_prefixes":[]}] }""")).Transports);
    }

    [Fact]
    public void MachineMismatchDisablesPortMappingAndCalibration()
    {
        var ports = PortTable.Read(File.ReadAllText(Fixture("ports.json")), new MachineIdentity { Manufacturer = "OTHER", Model = "Other" });
        var snapshot = SnapshotJson.Read(File.ReadAllText(Fixture("port-left-usba.json")));

        Assert.False(ports.IsEnabled);
        Assert.NotNull(ports.Warning);
        Assert.Contains(TopologyBuilder.Build(snapshot, ports).Roots, root => root.Id == "USB（未設定接口）");
        Assert.Throws<InvalidOperationException>(() => AttachmentDetector.Calibrate("usb-a-left", snapshot, snapshot, ports));
    }

    [Fact]
    public void DisplayCalibrationNeedsUniqueStableConnectorFields()
    {
        var ports = PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"hdmi","name":"HDMI","type":"hdmi","display_connectors":[]}] }""");
        var before = new Snapshot();
        var after = new Snapshot
        {
            DisplayPaths =
            [new DisplayPath
            {
                PathId = "new-path",
                AdapterName = @"\\?\DISPLAY#GPU",
                ConnectorInstance = 0,
                OutputTechnology = 5,
                Properties = { ["target_id"] = System.Text.Json.JsonDocument.Parse("256").RootElement.Clone() }
            }]
        };

        var result = AttachmentDetector.Calibrate("hdmi", before, after, ports);

        Assert.Equal("calibrated", result.Status);
        Assert.Equal(256, ports.FindInterface("hdmi")!.DisplayConnectors.Single().TargetId);
    }

    [Fact]
    public void JackSinkAndDisplayPathAdapterTargetMatchClassifiesDisplayAudio()
    {
        var ports = PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"hdmi","name":"HDMI","type":"hdmi","display_connectors":[{"adapter_device_path":"gpu-path","target_id":256,"connector_instance":0,"output_technology":5}]}]}""");
        var endpoint = new AudioEndpoint { EndpointId = "display-audio", Flow = "render", State = "active", DevnodeId = @"HDAUDIO\FUNC_01" };
        var sink = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["port_id"] = System.Text.Json.JsonDocument.Parse("{\"high\":1,\"low\":2}").RootElement.Clone(),
            ["target_id"] = System.Text.Json.JsonDocument.Parse("256").RootElement.Clone()
        };
        var path = new DisplayPath
        {
            PathId = "display-path",
            AdapterName = "gpu-path",
            AdapterLuid = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["high"] = System.Text.Json.JsonDocument.Parse("1").RootElement.Clone(),
                ["low"] = System.Text.Json.JsonDocument.Parse("2").RootElement.Clone()
            },
            ConnectorInstance = 0,
            OutputTechnology = 5,
            Properties = { ["target_id"] = System.Text.Json.JsonDocument.Parse("256").RootElement.Clone() }
        };
        var snapshot = new Snapshot
        {
            Endpoints = [endpoint],
            Devnodes = [new DeviceNode { InstanceId = endpoint.DevnodeId }],
            Jacks = [new JackRecord { EndpointId = endpoint.EndpointId, Sink = sink }],
            DisplayPaths = [path]
        };

        var result = TopologyBuilder.Build(snapshot, ports);

        Assert.Equal("B", Assert.Single(result.Endpoints).Category);
        Assert.Equal("hdmi", result.Endpoints[0].InterfaceId);
        Assert.Equal("hdmi", Assert.Single(result.DisplayGroups).InterfaceId);
    }

    [Fact]
    public void CurrentPathCalibrationSelectsTheTopmostNodeDirectlyBelowTheRootConnector()
    {
        var snapshot = new Snapshot
        {
            Endpoints = [new AudioEndpoint { EndpointId = "speaker", State = "active", Flow = "render", DevnodeId = @"SWD\MMDEVAPI\speaker" }],
            Devnodes =
            [
                new DeviceNode { InstanceId = @"SWD\MMDEVAPI\speaker", ParentId = @"USB\VID_SPEAKER\A" },
                new DeviceNode { InstanceId = @"USB\VID_SPEAKER\A", ParentId = @"USB\VID_DOCK_HUB\1", LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(4)#USB(2)"] },
                new DeviceNode { InstanceId = @"USB\VID_DOCK_HUB\1", ParentId = @"USB\ROOT_HUB30\1", LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(4)"] },
                new DeviceNode { InstanceId = @"USB\ROOT_HUB30\1", ParentId = @"PCI\XHCI\1" },
                new DeviceNode { InstanceId = @"PCI\XHCI\1", Name = "USB xHCI Controller" }
            ]
        };

        var attachment = AttachmentDetector.FindCurrentPathAttachment(snapshot, "speaker");

        Assert.NotNull(attachment);
        Assert.Equal(@"USB\VID_DOCK_HUB\1", attachment.InstanceId);

        var pciSnapshot = new Snapshot
        {
            Endpoints = [new AudioEndpoint { EndpointId = "pci-audio", State = "active", Flow = "render", DevnodeId = @"SWD\MMDEVAPI\pci-audio" }],
            Devnodes =
            [
                new DeviceNode { InstanceId = @"SWD\MMDEVAPI\pci-audio", ParentId = @"PCI\DEVICE\A" },
                new DeviceNode { InstanceId = @"PCI\DEVICE\A", ParentId = @"PCI\ROOTPORT\1", LocationPaths = ["PCIROOT(0)#PCI(0700)#PCI(0000)"] },
                new DeviceNode { InstanceId = @"PCI\ROOTPORT\1", Name = "PCI Express Root Port" }
            ]
        };
        Assert.Equal(@"PCI\DEVICE\A", AttachmentDetector.FindCurrentPathAttachment(pciSnapshot, "pci-audio")!.InstanceId);
    }

    [Fact]
    public void UsbClassificationUsesTheLongestCalibratedLocationPrefix()
    {
        var ports = PortTable.Read("""{"schema_version":1,"interfaces":[{"id":"broad","name":"Broad","type":"usb-a","usb_location_path_prefixes":["PCIROOT(0)"]},{"id":"specific","name":"Specific","type":"usb-c-tbt","usb_location_path_prefixes":["PCIROOT(0)#PCI(1400)"]}]}""");
        var snapshot = new Snapshot
        {
            Endpoints = [new AudioEndpoint { EndpointId = "usb", State = "active", Flow = "render", DevnodeId = @"USB\VID_1&PID_1\A" }],
            Devnodes = [new DeviceNode { InstanceId = @"USB\VID_1&PID_1\A", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(1)"] }]
        };

        Assert.Equal("specific", Assert.Single(TopologyBuilder.Build(snapshot, ports).Endpoints).InterfaceId);
    }

    [Fact]
    public void UncalibratedUsbDisplayAdapterStaysGroupedWithItsAudioDevice()
    {
        var physicalId = @"USB\VID_303A&PID_2986\SERIAL";
        var endpoint = new AudioEndpoint { EndpointId = "usb-audio", State = "active", Flow = "render", DevnodeId = @"SWD\MMDEVAPI\usb-audio" };
        var snapshot = new Snapshot
        {
            Endpoints = [endpoint],
            Devnodes =
            [
                new DeviceNode { InstanceId = endpoint.DevnodeId!, ParentId = physicalId },
                new DeviceNode { InstanceId = @"USB\VID_303A&PID_2986&MI_00\DISPLAY", ParentId = physicalId },
                new DeviceNode { InstanceId = physicalId, LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(1)"] }
            ],
            DisplayPaths = [new DisplayPath { PathId = "screen", TargetName = "Monitor", AdapterInstanceId = @"USB\VID_303A&PID_2986&MI_00\DISPLAY" }]
        };

        var tree = TopologyBuilder.Build(snapshot, PortTable.Empty());
        var root = Assert.Single(tree.Roots);
        var device = Assert.Single(root.Children);

        Assert.Equal("USB（未設定接口）", root.Name);
        Assert.Equal(physicalId, device.GroupKey);
        Assert.Equal(["endpoint", "display"], device.Children.Select(child => child.Kind));
        Assert.Equal("USB（未設定接口）", Assert.Single(tree.DisplayGroups).InterfaceId);
    }

    [Fact]
    public void CorruptOrUnsupportedConfigurationFilesReturnEmptyDefaultsWithErrors()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "ports.json"), "[]");
        File.WriteAllText(Path.Combine(temp.Path, "aliases.json"), "not json");
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), "{\"schema_version\":2}");

        var configuration = ConfigurationStore.Load(temp.Path);

        Assert.Equal(3, configuration.Errors.Count);
        Assert.Empty(configuration.Ports.Interfaces);
        Assert.Empty(configuration.Aliases.OriginalDescriptions);
        Assert.False(configuration.Settings.AutostartEnabled);
    }

    [Fact]
    public void ConfigurationFilesRoundTripAndAtomicWriteLeavesNoTemporaryFile()
    {
        using var temp = new TemporaryDirectory();
        var ports = PortTable.Read("""{"schema_version":1,"machine":{"manufacturer":"LENOVO","model":"T14","mtm":"21RU"},"interfaces":[{"id":"usb-a","name":"USB-A","type":"usb-a","usb_location_path_prefixes":["USB(1)"]}]}""");
        var aliases = new AliasStore();
        aliases.SetAlias(["ep"], new Dictionary<string, string> { ["ep"] = "Original" });
        var configuration = new AudioSwitchConfiguration
        {
            Ports = ports,
            Aliases = aliases,
            Settings = new AudioSwitchSettings { PlaybackHotkey = null, AutostartEnabled = false, FirstRunNotificationShown = true },
            Errors = []
        };

        ConfigurationStore.Save(temp.Path, configuration);
        var restored = ConfigurationStore.Load(temp.Path, new MachineIdentity { Manufacturer = "LENOVO", Model = "T14", MtM = "21RU-ABC" });

        Assert.Empty(restored.Errors);
        Assert.True(restored.Ports.IsEnabled);
        Assert.Equal("Original", restored.Aliases.OriginalDescriptions["ep"]);
        Assert.True(restored.Settings.FirstRunNotificationShown);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void AliasGroupingUsesUsbIdentityOrThePhysicalAudioJackAndKeepsOriginalNames()
    {
        TopologyEndpoint[] endpoints =
        [
            new("usb-render", "Realtek", "render", "A", "port-a", @"USB\DEVICE\SERIAL"),
            new("usb-capture", "Realtek", "capture", "A", "port-a", @"USB\DEVICE\SERIAL"),
            new("other-device", "Realtek", "render", "A", "port-a", @"USB\DEVICE\OTHER"),
            new("jack-render", "Realtek", "render", "C", "jack-combo", null),
            new("jack-capture", "Realtek", "capture", "C", "jack-combo", null),
            new("internal", "Realtek", "render", "C", null, null)
        ];
        var usbMembers = AliasEndpointGrouping.GetMembers(endpoints[0], endpoints);
        var jackMembers = AliasEndpointGrouping.GetMembers(endpoints[3], endpoints);
        var aliases = new AliasStore();
        aliases.SetAlias(usbMembers, new Dictionary<string, string> { ["usb-render"] = "Original out", ["usb-capture"] = "Original in" });
        aliases.SetAlias(usbMembers, new Dictionary<string, string> { ["usb-render"] = "Changed", ["usb-capture"] = "Changed" });

        Assert.Equal(["usb-render", "usb-capture"], usbMembers);
        Assert.Equal(["jack-render", "jack-capture"], jackMembers);
        Assert.Equal("Original out", aliases.OriginalDescriptions["usb-render"]);
        Assert.Equal("Original in", aliases.OriginalDescriptions["usb-capture"]);
        Assert.Equal(2, aliases.ClearAlias(usbMembers).Count);
    }

    private string Fixture(string name) => Path.Combine(_fixtures, name);

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            foreach (var file in Directory.GetFiles(Path))
            {
                File.Delete(file);
            }
            Directory.Delete(Path);
        }
    }
}
