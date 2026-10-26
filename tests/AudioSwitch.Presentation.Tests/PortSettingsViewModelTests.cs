using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class PortSettingsViewModelTests
{
    [Fact]
    public void AddingPortSelectsItSavesAndAsksForTheName()
    {
        var saves = 0;
        var model = CreateModel(new FakeSnapshotSource(new Snapshot()), PortTable.Empty(), () => saves++);
        var nameRequested = false;
        model.NameEditRequested += (_, _) => nameRequested = true;

        var port = model.Add(PortConnectorType.UsbC, PortEdge.Right, 0);

        Assert.NotNull(port);
        Assert.Equal(port.Id, model.SelectedInterface?.Id);
        Assert.Equal("右側 USB-C", model.NameText);
        Assert.True(nameRequested);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void PortFirstCalibrationShowsStepsThenCompletesAndOffersDeviceName()
    {
        var table = PortTable.Empty();
        var source = new FakeSnapshotSource(new Snapshot(), SpeakerSnapshot());
        var model = CreateModel(source, table);
        var port = model.Add(PortConnectorType.UsbA, PortEdge.Left, 0)!;

        model.BeginCalibration();

        Assert.Equal(PortCalibrationPhase.WaitingForDeviceAtPort, model.Phase);
        Assert.True(model.IsWaitingForDevice);
        Assert.Contains(model.CalibrationSteps, step => step.Contains("左側 USB-A", StringComparison.Ordinal));
        Assert.False(model.CanMoveSelected);

        model.CompleteDetection();

        Assert.Equal(PortCalibrationPhase.Idle, model.Phase);
        Assert.False(model.IsError);
        Assert.Equal(PortCalibrationState.Partial, model.Diagram.GetCalibrationState(port));
        Assert.True(model.CanEnterAlias);
        Assert.Equal("USB Speaker", model.DetectedEndpointNames);
    }

    [Fact]
    public void DeviceFirstFlowCalibratesThePortDroppedAfterDetection()
    {
        var table = PortTable.Empty();
        var model = CreateModel(new FakeSnapshotSource(new Snapshot(), SpeakerSnapshot()), table);

        model.BeginDetectNewDevice();
        model.CompleteDetection();

        Assert.True(model.IsWaitingForPlacement);
        var port = model.Add(PortConnectorType.UsbA, PortEdge.Rear, 0)!;

        Assert.Equal(PortCalibrationPhase.Idle, model.Phase);
        Assert.NotEmpty(port.UsbLocationPathPrefixes);
    }

    [Fact]
    public void FailedDetectionKeepsWaitingAndCancelReturnsToIdle()
    {
        var model = CreateModel(new FakeSnapshotSource(new Snapshot()), PortTable.Empty());
        model.Add(PortConnectorType.UsbA, PortEdge.Left, 0);
        model.BeginCalibration();

        model.CompleteDetection();

        Assert.True(model.IsError);
        Assert.Equal(PortCalibrationPhase.WaitingForDeviceAtPort, model.Phase);

        model.CancelCalibration();

        Assert.Equal(PortCalibrationPhase.Idle, model.Phase);
        Assert.False(model.IsError);
    }

    [Fact]
    public void AudioJackIsNeverCalibratedByPlugging()
    {
        var model = CreateModel(new FakeSnapshotSource(new Snapshot()), PortTable.Empty());
        model.Add(PortConnectorType.AudioJack, PortEdge.Right, 0);

        Assert.False(model.CanCalibrateSelected);
        model.BeginCalibration();
        Assert.Equal(PortCalibrationPhase.Idle, model.Phase);
    }

    [Fact]
    public void MovingSelectedPortWithKeyboardChangesOrderAndEdge()
    {
        var model = CreateModel(new FakeSnapshotSource(new Snapshot()), PortTable.Empty());
        var first = model.Add(PortConnectorType.UsbA, PortEdge.Left, int.MaxValue)!;
        model.Add(PortConnectorType.Hdmi, PortEdge.Left, int.MaxValue);
        model.Select(first.Id);

        model.MoveSelectedBy(1);
        Assert.Equal(1, first.Order);

        model.MoveSelectedToEdge(PortEdge.Front);
        Assert.Equal("front", first.Edge);
        Assert.Equal(first.Id, model.SelectedInterface?.Id);
    }

    [Fact]
    public void PortTableFromAnotherMachineCannotBeEdited()
    {
        var other = PortTable.Read(PortTable.Empty(new MachineIdentity { Manufacturer = "A", Model = "B" }).ToJson(),
            new MachineIdentity { Manufacturer = "C", Model = "D" });
        var model = CreateModel(new FakeSnapshotSource(new Snapshot()), other);

        Assert.False(model.IsEnabled);
        Assert.Null(model.Add(PortConnectorType.UsbA, PortEdge.Left, 0));
        Assert.True(model.IsError);
        Assert.False(model.ShowIdleHint);
    }

    private static PortSettingsViewModel CreateModel(IAudioTopologySnapshotSource source, PortTable table, Action? save = null)
    {
        var devices = new DeviceSettingsViewModel(new NullProbe(), new NullNameWriter(), source, table, new AliasStore(), () => { });
        return new PortSettingsViewModel(source, table, null, devices, save ?? (() => { }));
    }

    private static Snapshot SpeakerSnapshot() => new()
    {
        Devnodes =
        [
            new DeviceNode
            {
                InstanceId = "USB\\VID_1234&PID_5678\\A",
                ParentId = "USB\\ROOT_HUB30\\0",
                Name = "USB Speaker",
                LocationPaths = ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(1)", "ACPI(_SB_)#ACPI(PC00)#ACPI(XHCI)#ACPI(RHUB)#ACPI(HS01)"]
            },
            new DeviceNode { InstanceId = "SWD\\MMDEVAPI\\speaker", ParentId = "USB\\VID_1234&PID_5678\\A" }
        ],
        Endpoints =
        [
            new AudioEndpoint { EndpointId = "speaker", Flow = "render", State = "active", FriendlyName = "USB Speaker", DevnodeId = "SWD\\MMDEVAPI\\speaker" }
        ]
    };

    private sealed class FakeSnapshotSource(params Snapshot[] snapshots) : IAudioTopologySnapshotSource
    {
        private int _next;
        public Snapshot Capture() => snapshots[Math.Min(_next++, snapshots.Length - 1)];
    }

    private sealed class NullProbe : IAudioProbe
    {
        public Task PlayTestToneAsync(string endpointId, TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void StartPeakSampling(string endpointId) { }
        public float ReadPeakLevel(string endpointId) => 0;
        public Task StopPeakSamplingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class NullNameWriter : IEndpointNameWriter
    {
        public string ReadDeviceDescription(string endpointId) => endpointId;
        public string ReadFriendlyName(string endpointId) => endpointId;
        public void WriteDeviceDescription(string endpointId, string value) { }
        public void Dispose() { }
    }
}
