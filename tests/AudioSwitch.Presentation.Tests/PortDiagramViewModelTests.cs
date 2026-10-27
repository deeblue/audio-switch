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

        Assert.Equal("左側 USB-C", port.Name);
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
        Assert.Equal("右側 USB-C", port.Name);
    }

    [Fact]
    public void SuggestedNamesRemainUniqueForRepeatedConnectorTypesOnSameEdge()
    {
        var model = new PortDiagramViewModel(PortTable.Empty());

        var first = model.AddInterface(PortConnectorType.Hdmi, PortEdge.Left, 0);
        var second = model.AddInterface(PortConnectorType.Hdmi, PortEdge.Left, 0);

        Assert.Equal("左側 HDMI（前）", first.Name);
        Assert.Equal("左側 HDMI（後）", second.Name);
        Assert.NotEqual(first.Name, second.Name);
    }

    [Fact]
    public void ThreeSameTypePortsOnOneEdgeGetDistinctNamesThatFollowDeletes()
    {
        var model = new PortDiagramViewModel(PortTable.Empty());
        var rear = model.AddInterface(PortConnectorType.UsbA, PortEdge.Left, int.MaxValue);
        var middle = model.AddInterface(PortConnectorType.UsbA, PortEdge.Left, int.MaxValue);
        var front = model.AddInterface(PortConnectorType.UsbA, PortEdge.Left, int.MaxValue);

        Assert.Equal(["左側 USB-A（後）", "左側 USB-A（中）", "左側 USB-A（前）"], [rear.Name, middle.Name, front.Name]);

        model.DeleteInterface(middle.Id);

        Assert.Equal(["左側 USB-A（後）", "左側 USB-A（前）"], [rear.Name, front.Name]);
    }

    [Fact]
    public void DuplicateNamesFromEarlierVersionsAreMadeUniqueWhenLoaded()
    {
        var table = PortTable.Empty();
        table.AddInterface(new PortDefinition { Id = "c", Name = "左側 USB-C（後）", Type = "usb-c", Edge = "left", Order = 0 });
        table.AddInterface(new PortDefinition { Id = "jack-1", Name = "左側 3.5mm（前）", Type = "audio-jack", Edge = "left", Order = 1 });
        table.AddInterface(new PortDefinition { Id = "jack-2", Name = "左側 3.5mm（前）", Type = "audio-jack", Edge = "left", Order = 2 });
        table.AddInterface(new PortDefinition { Id = "named", Name = "會議室螢幕孔", Type = "hdmi", Edge = "left", Order = 3 });

        _ = new PortDiagramViewModel(table);

        Assert.Equal("左側 USB-C", table.FindInterface("c")!.Name);
        Assert.Equal("左側 3.5mm（後）", table.FindInterface("jack-1")!.Name);
        Assert.Equal("左側 3.5mm（前）", table.FindInterface("jack-2")!.Name);
        Assert.Equal("會議室螢幕孔", table.FindInterface("named")!.Name);
    }

    [Fact]
    public void ItemsListEveryEdgeInOrderWithCalibrationState()
    {
        var model = new PortDiagramViewModel(PortTable.Empty());
        var usb = model.AddInterface(PortConnectorType.UsbA, PortEdge.Left, 0);
        usb.CalibrationSources.Add(new PortCalibrationSource { Source = "校準", Transport = "USB 2" });
        usb.CalibrationSources.Add(new PortCalibrationSource { Source = "校準", Transport = "USB 3" });
        var partial = model.AddInterface(PortConnectorType.UsbC, PortEdge.Left, 1);
        partial.CalibrationSources.Add(new PortCalibrationSource { Source = "校準", Transport = "USB 2" });
        model.AddInterface(PortConnectorType.Hdmi, PortEdge.Rear, 0);
        model.AddInterface(PortConnectorType.AudioJack, PortEdge.Right, 0);

        var items = model.GetItems();

        Assert.Equal([PortEdge.Left, PortEdge.Left, PortEdge.Right, PortEdge.Rear], items.Select(item => item.Edge));
        Assert.Equal([0, 1], items.Where(item => item.Edge == PortEdge.Left).Select(item => item.Order));
        Assert.Equal(
            [PortCalibrationState.Calibrated, PortCalibrationState.Partial, PortCalibrationState.Automatic, PortCalibrationState.Uncalibrated],
            items.Select(item => item.State));
        Assert.Equal("左側 USB-A：已校準（USB 2、USB 3）", items[0].Description);
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

    [Fact]
    public void UsbCCalibrationStatusIncludesRecordedScreenOutputs()
    {
        var model = new PortDiagramViewModel(PortTable.Empty());
        var port = model.AddInterface(PortConnectorType.UsbC, PortEdge.Left, 0);
        port.DisplayConnectors.Add(new DisplayConnector { AdapterDevicePath = "gpu", TargetId = 1, ConnectorInstance = 0, OutputTechnology = 10 });
        Assert.Equal(PortCalibrationState.Partial, model.GetCalibrationState(port));
        Assert.Equal("只校準了顯示輸出，缺 USB 路徑", model.GetCalibrationStatus(port));

        foreach (var transport in new[] { "USB 2", "USB 3", "PCIe 隧道" })
            port.CalibrationSources.Add(new PortCalibrationSource { Source = "校準", Transport = transport });
        Assert.Equal(PortCalibrationState.Calibrated, model.GetCalibrationState(port));
        Assert.Equal("已校準（USB 2、USB 3、PCIe 隧道、顯示輸出）", model.GetCalibrationStatus(port));
    }
}
