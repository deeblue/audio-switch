using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class CalibrationWorkflowViewModelTests
{
    [Fact]
    public void PortFirstFlowCapturesBeforeThenCalibratesAfterInsertion()
    {
        var table = CreateTableWithPort(out var port);
        var source = new FakeSnapshotSource(new Snapshot(), UsbSnapshot("USB\\VID_1234&PID_5678\\A"));
        var model = new CalibrationWorkflowViewModel(source, table);
        model.BeginAtPort(port.Id);

        var result = model.CompleteDeviceStep();

        Assert.Equal("calibrated", result?.Status);
        Assert.Equal(CalibrationWorkflowState.Completed, model.State);
        Assert.Contains(port.UsbLocationPathPrefixes, path => path.Contains("USB(1)", StringComparison.Ordinal));
    }

    [Fact]
    public void DeviceFirstFlowCapturesDeviceThenWaitsForPortPlacement()
    {
        var table = CreateTableWithPort(out var port);
        var source = new FakeSnapshotSource(new Snapshot(), UsbSnapshot("USB\\VID_1234&PID_5678\\A"));
        var model = new CalibrationWorkflowViewModel(source, table);
        model.BeginBeforePortPlacement();

        model.CompleteDeviceStep();
        Assert.Equal(CalibrationWorkflowState.AwaitingPortPlacement, model.State);
        var result = model.SelectPort(port.Id);

        Assert.Equal("calibrated", result?.Status);
        Assert.Equal(CalibrationWorkflowState.Completed, model.State);
    }

    [Fact]
    public void MultipleSameTransportCandidatesAreAmbiguousAndDoNotMutatePort()
    {
        var table = CreateTableWithPort(out var port);
        var after = UsbSnapshot("USB\\VID_1234&PID_5678\\A", "USB\\VID_1234&PID_5678\\B");
        var model = new CalibrationWorkflowViewModel(new FakeSnapshotSource(new Snapshot(), after), table);
        model.BeginAtPort(port.Id);

        var result = model.CompleteDeviceStep();

        Assert.Equal("ambiguous", result?.Status);
        Assert.Contains("請只插入一個裝置後重試", model.ErrorMessage);
        Assert.Empty(port.UsbLocationPathPrefixes);
    }

    [Fact]
    public void CancelClearsPendingSnapshotsWithoutChangingPortTable()
    {
        var table = CreateTableWithPort(out var port);
        var source = new FakeSnapshotSource(new Snapshot());
        var model = new CalibrationWorkflowViewModel(source, table);
        model.BeginAtPort(port.Id);

        model.Cancel();

        Assert.Equal(CalibrationWorkflowState.Idle, model.State);
        Assert.Empty(port.UsbLocationPathPrefixes);
    }

    private static PortTable CreateTableWithPort(out PortDefinition port)
    {
        var table = PortTable.Empty();
        port = new PortDefinition { Id = "port-left", Name = "左側 USB-C", Type = "usb-c", Edge = "left", Order = 0 };
        table.AddInterface(port);
        return table;
    }

    private static Snapshot UsbSnapshot(params string[] instanceIds) => new()
    {
        Devnodes = instanceIds.Select((id, index) => new DeviceNode
        {
            InstanceId = id,
            ParentId = "USB\\ROOT_HUB30\\0",
            LocationPaths = [$"PCIROOT(0)#PCI(1400)#USBROOT(0)#USB({index + 1})#HS01"]
        }).ToList()
    };

    private sealed class FakeSnapshotSource(params Snapshot[] snapshots) : IAudioTopologySnapshotSource
    {
        private int _next;
        public Snapshot Capture() => snapshots[Math.Min(_next++, snapshots.Length - 1)];
    }
}
