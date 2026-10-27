using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class DevicePageViewModelTests
{
    [Fact]
    public void RefreshGroupsEndpointsByLocationAndMarksDefaults()
    {
        var model = CreateModel(out _);

        model.Refresh();

        var builtIn = Assert.Single(model.Roots);
        Assert.Equal("內建", builtIn.Name);
        Assert.Equal(DeviceNodeKind.Category, builtIn.Kind);
        var speaker = Assert.Single(builtIn.Children, node => node.Kind == DeviceNodeKind.Playback);
        var microphone = Assert.Single(builtIn.Children, node => node.Kind == DeviceNodeKind.Recording);
        Assert.True(speaker.IsDefault);
        Assert.False(microphone.IsDefault);
        Assert.Equal("內建", speaker.GroupName);
    }

    [Fact]
    public async Task ActionsFollowTheSelectedDeviceType()
    {
        var model = CreateModel(out _);
        model.Refresh();

        await model.SelectAsync(model.FindEndpoint("speaker"));
        Assert.True(model.CanTestTone);
        Assert.False(model.CanListen);
        Assert.False(model.CanIdentifyScreen);
        Assert.Equal("喇叭", model.Settings.AliasText);
        Assert.Equal("使用中", model.StateText);
        Assert.Contains("端點 ID：speaker", model.TechnicalDetails, StringComparison.Ordinal);

        await model.SelectAsync(model.FindEndpoint("microphone"));
        Assert.False(model.CanTestTone);
        Assert.True(model.CanListen);
    }

    [Fact]
    public async Task ListeningStopsWhenTheSelectedMicrophoneIsUnplugged()
    {
        var model = CreateModel(out var probe, out var source);
        model.Refresh();
        await model.SelectAsync(model.FindEndpoint("microphone"));
        await model.ToggleListeningAsync();
        Assert.True(model.Settings.IsListening);

        source.Snapshot.Endpoints.RemoveAll(endpoint => endpoint.EndpointId == "microphone");
        model.Refresh();

        Assert.Null(model.SelectedNode);
        Assert.False(model.Settings.IsListening);
        Assert.Equal(1, probe.StopCount);
    }

    [Fact]
    public void ScreenAudioWithoutAFoundScreenIsNotShownAsTheBuiltInSpeaker()
    {
        var model = CreateModel(out _, out var source);
        // Display audio whose screen was not matched falls back to category C; its jack is an HDMI jack, not integrated.
        source.Snapshot.Jacks.Add(new JackRecord
        {
            EndpointId = "speaker",
            Jack = new() { ["connection_type"] = Number(6), ["port_connection"] = Number(0) }
        });

        model.Refresh();

        Assert.Null(model.Active.Playback);
    }

    [Fact]
    public async Task BuiltInEndpointsHighlightTheirMarkerOnTheLaptopDrawing()
    {
        var model = CreateModel(out _);
        model.Refresh();

        await model.SelectAsync(model.FindEndpoint("speaker"));
        Assert.Equal(PortDiagramViewModel.BuiltInSpeakerId, model.SelectedInterfaceId);

        await model.SelectAsync(model.FindEndpoint("microphone"));
        Assert.Equal(PortDiagramViewModel.BuiltInMicrophoneId, model.SelectedInterfaceId);

        await model.SelectAsync(model.Roots[0]);
        Assert.Null(model.SelectedInterfaceId);
    }

    [Fact]
    public void CurrentDefaultsAreLocatedOnTheLaptopDrawing()
    {
        var model = CreateModel(out _);

        model.Refresh();

        Assert.Equal(PortDiagramViewModel.BuiltInSpeakerId, model.Active.Playback?.DiagramId);
        Assert.Equal("喇叭", model.Active.Playback?.Name);
        Assert.Null(model.Active.Recording);
    }

    [Fact]
    public void HeadphoneJackWithoutAPortAndUnmatchedUsbHaveNoPlaceOnTheDrawing()
    {
        var snapshot = new Snapshot
        {
            Endpoints =
            [
                new AudioEndpoint { EndpointId = "headphones", Flow = "render", State = "active", DevnodeId = @"SWD\MMDEVAPI\headphones" },
                new AudioEndpoint { EndpointId = "usb", Flow = "capture", State = "active", DevnodeId = @"SWD\MMDEVAPI\usb" }
            ],
            Devnodes =
            [
                new DeviceNode { InstanceId = @"HDAUDIO\CODEC" },
                new DeviceNode { InstanceId = @"SWD\MMDEVAPI\headphones", ParentId = @"HDAUDIO\CODEC" },
                new DeviceNode { InstanceId = @"USB\VID_1234&PID_0001\1", LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(1)"] },
                new DeviceNode { InstanceId = @"SWD\MMDEVAPI\usb", ParentId = @"USB\VID_1234&PID_0001\1" }
            ],
            Jacks = [new JackRecord { EndpointId = "headphones", Jack = new() { ["connection_type"] = Number(1), ["port_connection"] = Number(0) } }]
        };

        var active = ActiveDevices.Find(snapshot, TopologyBuilder.Build(snapshot, PortTable.Empty()), "headphones", "usb");

        Assert.Equal(ActiveDevices.None, active);
    }

    private static System.Text.Json.JsonElement Number(int value) => System.Text.Json.JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).RootElement.Clone();

    [Fact]
    public async Task ListeningStopsWhenAnotherNodeIsSelected()
    {
        var model = CreateModel(out var probe);
        model.Refresh();
        await model.SelectAsync(model.FindEndpoint("microphone"));

        await model.ToggleListeningAsync();
        Assert.True(model.IsListening);

        await model.SelectAsync(model.Roots[0]);

        Assert.False(model.IsListening);
        Assert.Equal(1, probe.StopCount);
        Assert.False(model.HasSelection && model.IsEndpointSelected);
    }

    [Fact]
    public async Task DockNameIsSavedPerModelAndCanBeRestored()
    {
        var snapshot = DockSnapshot();
        var source = new FakeSnapshotSource(snapshot);
        var table = PortTable.Empty();
        var probe = new FakeProbe();
        var settings = new DeviceSettingsViewModel(probe, new FakeNameWriter(), source, table, new AliasStore(), () => { });
        var names = new Dictionary<string, string>();
        var saves = 0;
        var model = new DevicePageViewModel(source, new FakeWriter(), probe, table, settings, names, () => saves++);
        model.Refresh();
        DeviceNodeViewModel Dock() => Assert.Single(Assert.Single(model.Roots).Children);

        await model.SelectAsync(Dock());
        Assert.True(model.IsDockSelected);
        Assert.False(model.CanRestoreDockName);
        Assert.Equal("Dock／Hub", model.DockNameText);

        model.DockNameText = "  桌上 Dock ";
        model.ApplyDockName();
        Assert.Equal("桌上 Dock", names["VID_03F0&PID_3BAF"]);
        Assert.Equal("桌上 Dock", Dock().Name);
        Assert.Same(Dock(), model.SelectedNode);
        Assert.True(model.CanRestoreDockName);

        model.RestoreDockName();
        Assert.Empty(names);
        Assert.Equal("Dock／Hub", Dock().Name);
        Assert.Equal(2, saves);
    }

    [Fact]
    public async Task DockNameThatCannotBeSavedIsNotKept()
    {
        var source = new FakeSnapshotSource(DockSnapshot());
        var table = PortTable.Empty();
        var probe = new FakeProbe();
        var settings = new DeviceSettingsViewModel(probe, new FakeNameWriter(), source, table, new AliasStore(), () => { });
        var names = new Dictionary<string, string> { ["VID_03F0&PID_3BAF"] = "桌上 Dock" };
        var model = new DevicePageViewModel(source, new FakeWriter(), probe, table, settings, names,
            () => throw new IOException("locked"));
        model.Refresh();
        await model.SelectAsync(Assert.Single(Assert.Single(model.Roots).Children));

        model.DockNameText = "會議室";
        model.ApplyDockName();
        Assert.Equal("桌上 Dock", names["VID_03F0&PID_3BAF"]);
        Assert.True(model.IsError);

        model.RestoreDockName();
        Assert.Equal("桌上 Dock", names["VID_03F0&PID_3BAF"]);
    }

    private static Snapshot DockSnapshot() => new()
    {
        Endpoints = [new AudioEndpoint { EndpointId = "usb-speaker", Flow = "render", State = "active", FriendlyName = "USB 喇叭", DevnodeId = @"SWD\MMDEVAPI\usb-speaker" }],
        Devnodes =
        [
            new DeviceNode { InstanceId = @"USB\ROOT_HUB30\ROOT" },
            new DeviceNode { InstanceId = @"USB\VID_03F0&PID_3BAF\DOCK", ParentId = @"USB\ROOT_HUB30\ROOT", LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(5)"] },
            new DeviceNode { InstanceId = @"USB\VID_303A&PID_2986\SPK", ParentId = @"USB\VID_03F0&PID_3BAF\DOCK", Name = "speaker", LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(5)#USB(1)"] },
            new DeviceNode { InstanceId = @"SWD\MMDEVAPI\usb-speaker", ParentId = @"USB\VID_303A&PID_2986\SPK" }
        ]
    };

    private static DevicePageViewModel CreateModel(out FakeProbe probe) => CreateModel(out probe, out _);

    private static DevicePageViewModel CreateModel(out FakeProbe probe, out FakeSnapshotSource source)
    {
        var snapshot = new Snapshot
        {
            Endpoints =
            [
                new AudioEndpoint { EndpointId = "speaker", Flow = "render", State = "active", FriendlyName = "喇叭", DevnodeId = "SWD\\MMDEVAPI\\speaker", FormFactor = 1 },
                new AudioEndpoint { EndpointId = "microphone", Flow = "capture", State = "active", FriendlyName = "麥克風", DevnodeId = "SWD\\MMDEVAPI\\microphone", FormFactor = 4 }
            ],
            Devnodes =
            [
                new DeviceNode { InstanceId = "HDAUDIO\\CODEC", Name = "Realtek(R) Audio" },
                new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\speaker", ParentId = "HDAUDIO\\CODEC" },
                new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\microphone", ParentId = "HDAUDIO\\CODEC" }
            ]
        };
        source = new FakeSnapshotSource(snapshot);
        var table = PortTable.Empty();
        probe = new FakeProbe();
        var settings = new DeviceSettingsViewModel(probe, new FakeNameWriter(), source, table, new AliasStore(), () => { });
        return new DevicePageViewModel(source, new FakeWriter(), probe, table, settings);
    }

    private sealed class FakeSnapshotSource(Snapshot snapshot) : IAudioTopologySnapshotSource
    {
        public Snapshot Snapshot { get; } = snapshot;
        public Snapshot Capture() => Snapshot;
    }

    private sealed class FakeWriter : IDefaultEndpointWriter
    {
        public string? GetDefaultEndpointId(AudioEndpointFlow flow, AudioEndpointRole role) => flow == AudioEndpointFlow.Render ? "speaker" : null;
        public EndpointSwitchResult SetDefaultEndpoint(string endpointId, AudioEndpointFlow flow) => throw new NotSupportedException();
        public EndpointRoleChangeResult SetDefaultEndpointRole(string endpointId, AudioEndpointFlow flow, AudioEndpointRole role) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class FakeNameWriter : IEndpointNameWriter
    {
        public string ReadDeviceDescription(string endpointId) => endpointId == "speaker" ? "喇叭" : "麥克風";
        public string ReadFriendlyName(string endpointId) => ReadDeviceDescription(endpointId);
        public void WriteDeviceDescription(string endpointId, string value) => throw new NotSupportedException();
        public void Dispose() { }
    }

    internal sealed class FakeProbe : IAudioProbe
    {
        public int StopCount { get; private set; }
        public Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void StartPeakSampling(string endpointId) { }
        public float ReadPeakLevel(string endpointId) => 0.3f;
        public Task StopPeakSamplingAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }
}
