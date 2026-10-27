using System.Text.Json;
using AudioSwitch.Core;
using AudioSwitch.Platform.Windows;
using AudioSwitch.Presentation;

interface IScreenshotFixture : IAudioTopologySnapshotSource, IAudioEndpointSource, IDisposable
{
    MachineIdentity Machine { get; }
    PortTable CreatePorts();
    AudioSwitchConfiguration LoadConfiguration(string dataDirectory);
}

/// <summary>Fixed, reproducible device scenarios. Every capture gets a fresh copy, so one screenshot cannot change another.</summary>
sealed class Fixture(Snapshot snapshot, Func<PortTable> ports) : IScreenshotFixture
{
    public Snapshot Snapshot { get; } = snapshot;
    public MachineIdentity Machine => ProgramEntry.FixtureMachine;
    public IReadOnlyList<PlatformError> LastErrors => [];
    public event EventHandler<EndpointNotificationEventArgs>? EndpointChanged { add { } remove { } }
    public event EventHandler<EndpointNotificationEventArgs>? DefaultEndpointChanged { add { } remove { } }
    public Snapshot Capture() => Clone(Snapshot);
    public IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints() => Capture().Endpoints;
    public PortTable CreatePorts() => Fixtures.InCurrentLanguage(ports());
    public AudioSwitchConfiguration LoadConfiguration(string dataDirectory) =>
        new() { Ports = CreatePorts(), Aliases = new AliasStore(), Settings = new AudioSwitchSettings(), Errors = [] };
    public void Dispose() { }

    /// <summary>Simulates plugging a USB speaker into a port after a calibration has started.</summary>
    public void AttachUsbSpeaker() => Fixtures.AddUsbSpeaker(Snapshot);

    public static Snapshot Clone(Snapshot source) => JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(source))!;
}

sealed class LiveFixture : IScreenshotFixture
{
    private readonly WindowsAudioEndpointSource _endpoints = new();
    private readonly WindowsAudioTopologySnapshotSource _snapshots;
    private readonly string _dataDirectory;

    public LiveFixture(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        _snapshots = new WindowsAudioTopologySnapshotSource(endpoints: _endpoints);
        Machine = new WindowsMachineIdentitySource().ReadIdentity();
    }

    public MachineIdentity Machine { get; }
    public IReadOnlyList<PlatformError> LastErrors => _endpoints.LastErrors;
    public event EventHandler<EndpointNotificationEventArgs>? EndpointChanged { add { } remove { } }
    public event EventHandler<EndpointNotificationEventArgs>? DefaultEndpointChanged { add { } remove { } }
    public Snapshot Capture() => _snapshots.Capture();
    public IReadOnlyList<AudioEndpoint> EnumerateActiveEndpoints() => _endpoints.EnumerateActiveEndpoints();
    public PortTable CreatePorts() => Fixtures.InCurrentLanguage(ConfigurationStore.Load(_dataDirectory, Machine).Ports);
    public AudioSwitchConfiguration LoadConfiguration(string dataDirectory)
    {
        var configuration = ConfigurationStore.Load(dataDirectory, Machine);
        Fixtures.InCurrentLanguage(configuration.Ports);
        return configuration;
    }
    public void Dispose() => _endpoints.Dispose();
}

static class Fixtures
{
    /// <summary>Suggested port names follow the interface language, as the app does when it starts.</summary>
    public static PortTable InCurrentLanguage(PortTable ports)
    {
        new PortDiagramViewModel(ports).RelabelAutoNames();
        return ports;
    }

    const string Codec = "HDAUDIO\\CODEC\\INTERNAL";

    public static Fixture Standard() => new(StandardSnapshot(), StandardPorts);

    public static Fixture BuiltInOnly()
    {
        var snapshot = StandardSnapshot();
        snapshot.Endpoints.RemoveAll(endpoint => endpoint.EndpointId.Contains("headset", StringComparison.Ordinal));
        snapshot.Jacks.Clear();
        return new Fixture(snapshot, StandardPorts);
    }

    public static Fixture Empty() => new(new Snapshot { Machine = ProgramEntry.FixtureMachine }, StandardPorts);

    public static Fixture WithUnconfiguredUsb()
    {
        var snapshot = StandardSnapshot();
        AddUsbSpeaker(snapshot);
        return new Fixture(snapshot, StandardPorts);
    }

    public static Fixture LongNames()
    {
        var snapshot = StandardSnapshot();
        foreach (var endpoint in snapshot.Endpoints)
            endpoint.FriendlyName = $"會議室角落那台很長名稱的 USB 喇叭與麥克風組合（{endpoint.FriendlyName}）Realtek(R) Audio High Definition";
        return new Fixture(snapshot, StandardPorts);
    }

    public static Fixture ManyPorts() => new(StandardSnapshot(), () =>
    {
        var ports = StandardPorts();
        var types = new[] { "usb-a", "usb-c", "hdmi", "usb-a", "usb-c", "audio-jack" };
        for (var index = 0; index < types.Length; index++)
            ports.AddInterface(new PortDefinition { Id = $"extra-{index}", Name = $"額外接口 {index + 1}", Type = types[index], Edge = "left", Order = 99 });
        return ports;
    });

    public static Fixture OtherMachine() => new(StandardSnapshot(), () =>
        PortTable.Read(StandardPorts().ToJson(), new MachineIdentity { Manufacturer = "Contoso", Model = "Desk 9" }));

    public static Fixture MultipleDevices()
    {
        var snapshot = StandardSnapshot();
        const string dock = "USB\\VID_1234&PID_0001\\DOCK";
        const string hub = "USB\\VID_1234&PID_0002\\HUB";
        const string speaker = "USB\\VID_1234&PID_0003\\SPEAKER";
        const string monitor = "USB\\VID_1234&PID_0004\\MONITOR";
        snapshot.Devnodes.AddRange([
            new DeviceNode { InstanceId = dock, ParentId = "USB\\ROOT_HUB30\\1", Name = "USB-C Dock", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)"] },
            new DeviceNode { InstanceId = hub, ParentId = dock, Name = "USB Hub", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)#USB(1)"] },
            new DeviceNode { InstanceId = speaker, ParentId = hub, Name = "Jabra Speak 510", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)#USB(1)#USB(2)"] },
            new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\dock-speaker", ParentId = speaker },
            new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\dock-speaker-mic", ParentId = speaker },
            new DeviceNode { InstanceId = monitor, ParentId = "USB\\ROOT_HUB30\\2", Name = "USB-C 可攜式螢幕", LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(8)"] },
            new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\monitor-speaker", ParentId = monitor },
            new DeviceNode { InstanceId = "BTHENUM\\DEV_123456", Name = "Bluetooth Audio" },
            new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\bluetooth", ParentId = "BTHENUM\\DEV_123456" },
            new DeviceNode { InstanceId = "ROOT\\MEDIA\\VIRTUAL_AUDIO", Name = "Virtual Audio" },
            new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\virtual", ParentId = "ROOT\\MEDIA\\VIRTUAL_AUDIO" },
            new DeviceNode { InstanceId = "PCI\\VEN_1234&DEV_0001", Name = "Unknown Audio" },
            new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\unknown", ParentId = "PCI\\VEN_1234&DEV_0001" }
        ]);
        snapshot.Endpoints.AddRange([
            Endpoint("render-dock-speaker", "render", "Jabra Speak 510", "SWD\\MMDEVAPI\\dock-speaker"),
            Endpoint("capture-dock-speaker", "capture", "Jabra Speak 510 麥克風", "SWD\\MMDEVAPI\\dock-speaker-mic"),
            Endpoint("render-monitor-speaker", "render", "USB-C 螢幕喇叭", "SWD\\MMDEVAPI\\monitor-speaker"),
            Endpoint("render-bluetooth", "render", "WH-1000XM5", "SWD\\MMDEVAPI\\bluetooth"),
            Endpoint("render-virtual", "render", "CABLE Input", "SWD\\MMDEVAPI\\virtual"),
            Endpoint("render-unknown", "render", "未知的音訊裝置", "SWD\\MMDEVAPI\\unknown")
        ]);
        snapshot.DisplayPaths.Add(new DisplayPath
        {
            PathId = "usb-monitor-path",
            AdapterInstanceId = monitor,
            TargetName = "USB-C 可攜式螢幕",
            Properties = new Dictionary<string, JsonElement> { ["target_id"] = Json("3") }
        });
        return new Fixture(snapshot, StandardPorts);
    }

    public static void AddUsbSpeaker(Snapshot snapshot)
    {
        if (snapshot.Endpoints.Any(endpoint => endpoint.EndpointId == "render-new-speaker")) return;
        snapshot.Devnodes.Add(new DeviceNode
        {
            InstanceId = "USB\\VID_4321&PID_0001\\NEW_SPEAKER",
            ParentId = "USB\\ROOT_HUB30\\3",
            Name = "USB Speaker",
            LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(12)", "ACPI(_SB_)#ACPI(PC00)#ACPI(XHCI)#ACPI(RHUB)#ACPI(HS12)"]
        });
        snapshot.Devnodes.Add(new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\new-speaker", ParentId = "USB\\VID_4321&PID_0001\\NEW_SPEAKER" });
        snapshot.Endpoints.Add(Endpoint("render-new-speaker", "render", "USB Speaker", "SWD\\MMDEVAPI\\new-speaker"));
    }

    public static PortTable StandardPorts()
    {
        var ports = PortTable.Empty(ProgramEntry.FixtureMachine);
        ports.AddInterface(Port("left-usb-c", "左側 USB-C", "usb-c", "left", 0, "USB(1)", "USB 2", "USB 3", "PCIe 隧道"));
        ports.AddInterface(Port("left-hdmi", "左側 HDMI", "hdmi", "left", 1, null));
        ports.AddInterface(Port("left-usb-a", "左側 USB-A", "usb-a", "left", 2, "USB(3)", "USB 2", "USB 3"));
        ports.AddInterface(Port("right-usb-a", "右側 USB-A", "usb-a", "right", 0, "USB(4)", "USB 2"));
        ports.AddInterface(Port("right-audio", "右側 3.5mm", "audio-jack", "right", 1, null));
        ports.AddInterface(Port("rear-usb-c", "後側 USB-C", "usb-c", "rear", 0, null));
        return ports;
    }

    static PortDefinition Port(string id, string name, string type, string edge, int order, string? usbPort, params string[] transports) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        Edge = edge,
        Order = order,
        Source = "user",
        PositionSource = "user",
        UsbLocationPathPrefixes = usbPort is null ? [] : [$"PCIROOT(0)#PCI(1400)#USBROOT(0)#{usbPort}"],
        CalibrationSources = transports.Select(transport => new PortCalibrationSource
        {
            Source = "device-tree",
            Transport = transport,
            LocationPaths = [$"PCIROOT(0)#PCI(1400)#USBROOT(0)#{usbPort}"]
        }).ToList()
    };

    static Snapshot StandardSnapshot() => new()
    {
        Machine = ProgramEntry.FixtureMachine,
        Endpoints =
        [
            Endpoint("render-speakers", "render", "喇叭 (Realtek(R) Audio)", "SWD\\MMDEVAPI\\speaker", isDefault: true),
            Endpoint("render-headset", "render", "耳機 (Realtek(R) Audio)", "SWD\\MMDEVAPI\\headset"),
            Endpoint("capture-array", "capture", "麥克風陣列 (Realtek(R) Audio)", "SWD\\MMDEVAPI\\array", isDefault: true),
            Endpoint("capture-headset", "capture", "耳機麥克風 (Realtek(R) Audio)", "SWD\\MMDEVAPI\\headset-mic")
        ],
        Devnodes =
        [
            new() { InstanceId = "SWD\\MMDEVAPI\\speaker", ParentId = Codec },
            new() { InstanceId = "SWD\\MMDEVAPI\\headset", ParentId = Codec },
            new() { InstanceId = "SWD\\MMDEVAPI\\array", ParentId = Codec },
            new() { InstanceId = "SWD\\MMDEVAPI\\headset-mic", ParentId = Codec },
            new() { InstanceId = Codec, Name = "Realtek(R) Audio" }
        ],
        Jacks = [PhysicalJack("render-headset"), PhysicalJack("capture-headset")]
    };

    static AudioEndpoint Endpoint(string id, string flow, string name, string node, bool isDefault = false) => new()
    {
        EndpointId = id,
        Flow = flow,
        State = "active",
        FriendlyName = name,
        DevnodeId = node,
        FormFactor = flow == "render" ? 1 : 4,
        Defaults = new Dictionary<string, bool> { ["multimedia"] = isDefault, ["console"] = isDefault, ["communications"] = isDefault }
    };

    static JackRecord PhysicalJack(string endpointId)
    {
        using var data = JsonDocument.Parse("{\"connection_type\":1,\"port_connection\":0}");
        return new JackRecord
        {
            EndpointId = endpointId,
            Jack = data.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
        };
    }

    static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }
}

sealed class SafeEndpointWriter(IScreenshotFixture source) : IDefaultEndpointWriter
{
    public string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role)
    {
        var flowName = flow == AudioEndpointFlow.Render ? "render" : "capture";
        var roleName = role.ToString().ToLowerInvariant();
        return source.Capture().Endpoints.FirstOrDefault(endpoint =>
            endpoint.Flow.Equals(flowName, StringComparison.OrdinalIgnoreCase) &&
            endpoint.Defaults.TryGetValue(roleName, out var isDefault) && isDefault)?.EndpointId;
    }

    public EndpointSwitchResult SetDefaultEndpoint(string endpointId, AudioEndpointFlow flow) => throw new InvalidOperationException("Screenshot runs cannot change Windows defaults.");
    public EndpointRoleChangeResult SetDefaultEndpointRole(string endpointId, AudioEndpointFlow flow, AudioEndpointRole role) => throw new InvalidOperationException("Screenshot runs cannot change Windows defaults.");
    public void Dispose() { }
}

sealed class SafeProbe : IAudioProbe
{
    public Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Screenshot runs cannot play audio.");
    public void StartPeakSampling(string endpointId) { }
    public float ReadPeakLevel(string endpointId) => 0.42f;
    public Task StopPeakSamplingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Dispose() { }
}

sealed class SafeNameWriter(IScreenshotFixture source) : IEndpointNameWriter
{
    public string ReadDeviceDescription(string endpointId)
    {
        var name = source.Capture().Endpoints.FirstOrDefault(endpoint => endpoint.EndpointId == endpointId)?.FriendlyName ?? "音訊裝置";
        var adapter = name.IndexOf(" (", StringComparison.Ordinal);
        return adapter > 0 ? name[..adapter] : name;
    }

    public string ReadFriendlyName(string endpointId) => ReadDeviceDescription(endpointId);
    public void WriteDeviceDescription(string endpointId, string value) => throw new InvalidOperationException("Screenshot runs cannot rename endpoints.");
    public void Dispose() { }
}

sealed class SafeHotkeyService : IHotkeyService
{
    public HotkeyRegistrationResult Register(IntPtr targetWindow, string? playbackHotkey, string? recordingHotkey) =>
        playbackHotkey is null && recordingHotkey is null ? new(true, null) : new(false, "這組快捷鍵已被其他程式使用。");
    public void Unregister() { }
    public void Dispose() { }
}

sealed class SafeAutostartService : IAutostartService
{
    public void SetEnabled(bool enabled) => throw new InvalidOperationException("Screenshot runs cannot change startup settings.");
}
