using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class DeviceSettingsViewModelTests
{
    [Fact]
    public async Task SelectionPrefillsAliasFromDeviceDescription()
    {
        var writer = new FakeNameWriter { Descriptions = { ["one"] = "Device Description" } };
        var model = CreateModel(writer: writer);

        await model.SelectEndpointAsync(Endpoint("one", "Friendly Name", null));

        Assert.Equal("Device Description", model.AliasText);
        Assert.Empty(writer.Writes);
    }

    [Fact]
    public async Task ApplyingUnchangedAliasDoesNotWriteOrSave()
    {
        var writer = new FakeNameWriter { Descriptions = { ["one"] = "Existing Description" } };
        var saves = 0;
        var model = CreateModel(writer: writer, save: () => saves++);
        await model.SelectEndpointAsync(Endpoint("one", "Different Friendly Name", null));

        Assert.True(model.ApplyAlias([Endpoint("one", "Different Friendly Name", null)]));

        Assert.Empty(writer.Writes);
        Assert.Empty(model.Aliases.OriginalDescriptions);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task FailedAliasWriteRestoresEarlierMembersInReverseAndDoesNotSave()
    {
        var writer = new FakeNameWriter { Descriptions = { ["one"] = "Original One", ["two"] = "Original Two" }, FailOnEndpoint = "two" };
        var saves = 0;
        var model = CreateModel(writer: writer, save: () => saves++);
        var endpoints = new[] { Endpoint("one", "One", "usb-group"), Endpoint("two", "Two", "usb-group") };
        await model.SelectEndpointAsync(endpoints[0]);
        model.AliasText = "Shared Alias";

        Assert.False(model.ApplyAlias(endpoints));

        Assert.Equal("Original One", writer.Descriptions["one"]);
        Assert.Equal("Original Two", writer.Descriptions["two"]);
        Assert.Equal([("one", "Shared Alias"), ("two", "Shared Alias"), ("one", "Original One")], writer.Writes);
        Assert.Empty(model.Aliases.OriginalDescriptions);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task ApplyAndClearAliasUseGroupMembersAndPersistOriginalNames()
    {
        var writer = new FakeNameWriter { Descriptions = { ["one"] = "Original One", ["two"] = "Original Two" } };
        var saves = 0;
        var model = CreateModel(writer: writer, save: () => saves++);
        var endpoints = new[] { Endpoint("one", "One", "usb-group"), Endpoint("two", "Two", "usb-group") };
        await model.SelectEndpointAsync(endpoints[0]);
        model.AliasText = "Dock Headset";

        Assert.True(model.ApplyAlias(endpoints));
        Assert.Equal("Dock Headset", writer.Descriptions["one"]);
        Assert.Equal("Dock Headset", writer.Descriptions["two"]);
        Assert.Equal("Original One", model.Aliases.OriginalDescriptions["one"]);
        Assert.Equal("Original Two", model.Aliases.OriginalDescriptions["two"]);

        Assert.True(model.ClearAlias(endpoints));
        Assert.Equal("Original One", writer.Descriptions["one"]);
        Assert.Equal("Original Two", writer.Descriptions["two"]);
        Assert.Empty(model.Aliases.OriginalDescriptions);
        Assert.Equal(2, saves);
    }

    [Fact]
    public async Task CurrentPathCalibrationUsesSelectedEndpointAndSavesSuccessfulMapping()
    {
        var snapshot = new Snapshot
        {
            Endpoints = [new AudioEndpoint { EndpointId = "one", Flow = "render", State = "active", DevnodeId = @"SWD\MMDEVAPI\one" }],
            Devnodes =
            [
                new DeviceNode { InstanceId = @"SWD\MMDEVAPI\one", ParentId = @"USB\VID_AUDIO\1" },
                new DeviceNode { InstanceId = @"USB\VID_AUDIO\1", ParentId = @"USB\ROOT_HUB30\1", LocationPaths = ["PCIROOT(0)#USBROOT(0)#USB(1)#HS01"] },
                new DeviceNode { InstanceId = @"USB\ROOT_HUB30\1" }
            ]
        };
        var ports = PortTable.Empty();
        ports.AddInterface(new PortDefinition { Id = "port", Name = "USB-C", Type = "usb-c", Edge = "left", Order = 0 });
        var saves = 0;
        var model = CreateModel(snapshotSource: new FakeSnapshotSource(snapshot), ports: ports, save: () => saves++);
        await model.SelectEndpointAsync(Endpoint("one", "Speaker", null));

        var result = model.CalibrateSelectedDevice("port");

        Assert.Equal("calibrated", result?.Status);
        Assert.Equal(["PCIROOT(0)#USBROOT(0)#USB(1)#HS01"], ports.FindInterface("port")!.UsbLocationPathPrefixes);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task ListeningStopsWhenToggledAgainPageLeavesOrWindowCloses()
    {
        var probe = new FakeProbe();
        var model = CreateModel(probe: probe);

        Assert.True(await model.ToggleListeningAsync("capture-one"));
        Assert.True(model.IsListening);
        Assert.Equal("capture-one", model.ListeningEndpointId);
        Assert.False(await model.ToggleListeningAsync("capture-one"));
        Assert.False(model.IsListening);

        Assert.True(await model.ToggleListeningAsync("capture-one"));
        await model.SelectEndpointAsync(Endpoint("capture-two", "Microphone Two", null));
        Assert.False(model.IsListening);
        Assert.Equal(2, probe.StopCount);

        Assert.True(await model.ToggleListeningAsync("capture-two"));
        await model.SetDevicePageActiveAsync(false);
        Assert.False(model.IsListening);
        Assert.Equal(3, probe.StopCount);

        Assert.True(await model.ToggleListeningAsync("capture-two"));
        await model.CloseAsync();
        Assert.False(model.IsListening);
        Assert.Equal(4, probe.StopCount);
        Assert.Equal(4, probe.StartCount);
    }

    private static DeviceSettingsViewModel CreateModel(FakeNameWriter? writer = null, FakeProbe? probe = null,
        IAudioTopologySnapshotSource? snapshotSource = null, PortTable? ports = null, Action? save = null) =>
        new(probe ?? new FakeProbe(), writer ?? new FakeNameWriter(), snapshotSource ?? new FakeSnapshotSource(new Snapshot()),
            ports ?? PortTable.Empty(), new AliasStore(), save ?? (() => { }));

    private static TopologyEndpoint Endpoint(string id, string name, string? groupKey) =>
        new(id, name, "capture", "A", null, groupKey);

    private sealed class FakeNameWriter : IEndpointNameWriter
    {
        public Dictionary<string, string> Descriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string EndpointId, string Value)> Writes { get; } = [];
        public string? FailOnEndpoint { get; init; }
        public string ReadDeviceDescription(string endpointId) => Descriptions[endpointId];
        public string ReadFriendlyName(string endpointId) => "Friendly Name";
        public void WriteDeviceDescription(string endpointId, string value)
        {
            Writes.Add((endpointId, value));
            if (endpointId == FailOnEndpoint) throw new InvalidOperationException("write failed");
            Descriptions[endpointId] = value;
        }
        public void Dispose() { }
    }

    private sealed class FakeProbe : IAudioProbe
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void StartPeakSampling(string endpointId) => StartCount++;
        public float ReadPeakLevel(string endpointId) => 0.5f;
        public Task StopPeakSamplingAsync(CancellationToken cancellationToken = default) { StopCount++; return Task.CompletedTask; }
        public void Dispose() { }
    }

    private sealed class FakeSnapshotSource(Snapshot snapshot) : IAudioTopologySnapshotSource
    {
        public Snapshot Capture() => snapshot;
    }
}
