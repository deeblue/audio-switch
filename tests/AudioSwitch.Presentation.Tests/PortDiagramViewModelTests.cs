using AudioSwitch.Core;
using AudioSwitch.Presentation;

namespace AudioSwitch.Presentation.Tests;

public sealed class PortDiagramViewModelTests
{
    [Fact]
    public void AddUsesPositionBasedNameAndStoresMachineIdentity()
    {
        var machine = new MachineIdentity { Manufacturer = "Vendor", Model = "Laptop" };
        var table = PortTable.Empty(machine);
        var model = new PortDiagramViewModel(table);

        var port = model.AddInterface(PortConnectorType.UsbC, PortEdge.Left, 0);

        Assert.Equal("左側 USB-C（後）", port.Name);
        Assert.Equal("left", port.Edge);
        Assert.Equal(0, port.Order);
        Assert.Contains("Vendor", table.ToJson());
    }

    [Fact]
    public void MovingAutoNamedPortUpdatesEdgeOrderAndSuggestedName()
    {
        var model = new PortDiagramViewModel(PortTable.Empty());
        var port = model.AddInterface(PortConnectorType.UsbC, PortEdge.Left, 0);
        model.AddInterface(PortConnectorType.Hdmi, PortEdge.Right, 0);

        model.MoveInterface(port.Id, PortEdge.Right, 1);

        Assert.Equal("right", port.Edge);
        Assert.Equal(1, port.Order);
        Assert.Equal("右側 USB-C（前）", port.Name);
    }

    [Fact]
    public void MovePreservesUserRenamedPortAndDeleteRemovesIt()
    {
        var table = PortTable.Empty();
        var model = new PortDiagramViewModel(table);
        var port = model.AddInterface(PortConnectorType.Hdmi, PortEdge.Rear, 0);
        model.RenameInterface(port.Id, "會議室螢幕孔");

        model.MoveInterface(port.Id, PortEdge.Front, 0);
        Assert.Equal("會議室螢幕孔", port.Name);

        model.DeleteInterface(port.Id);
        Assert.Empty(model.Interfaces);
        Assert.Empty(table.Interfaces);
    }

    [Fact]
    public void CalibrationStatusShowsMissingAndRecordedTransports()
    {
        var model = new PortDiagramViewModel(PortTable.Empty());
        var port = model.AddInterface(PortConnectorType.UsbC, PortEdge.Left, 0);
        Assert.Equal("未校準", model.GetCalibrationStatus(port));

        port.CalibrationSources.Add(new PortCalibrationSource { Source = "校準", Transport = "USB 2" });
        Assert.Equal("缺部分路徑（缺 USB 3、缺 PCIe 隧道）", model.GetCalibrationStatus(port));
    }
}
