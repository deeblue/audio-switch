using System.Windows;
using AudioSwitch.App.Views;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Tests;

public sealed class LaptopDiagramDropTests
{
    private static readonly PortDiagramItem[] LeftPorts =
    [
        Item("a", PortEdge.Left, 0),
        Item("b", PortEdge.Left, 1),
        Item("c", PortEdge.Left, 2)
    ];

    [Fact]
    public void DropBetweenTwoPortsInsertsBetweenThem()
    {
        var between = Midpoint(LeftPorts, "a", "b");

        Assert.Equal((PortEdge.Left, 1), LaptopDiagramView.DropTarget(between with { X = between.X - 6 }, LeftPorts, null));
    }

    [Fact]
    public void DropPastTheLastPortAppends()
    {
        var last = LaptopDiagramView.PortCenter(LeftPorts, LeftPorts[2]);

        Assert.Equal((PortEdge.Left, 3), LaptopDiagramView.DropTarget(last with { Y = last.Y + 12 }, LeftPorts, null));
    }

    [Fact]
    public void MovingAPortDownCountsOnlyTheOtherPorts()
    {
        var betweenBAndC = Midpoint(LeftPorts, "b", "c");

        Assert.Equal((PortEdge.Left, 1), LaptopDiagramView.DropTarget(betweenBAndC, LeftPorts, "a"));
    }

    [Fact]
    public void NearestChassisEdgeWins()
    {
        var rear = LaptopDiagramView.DropTarget(new Point(220, 50), LeftPorts, null);
        var right = LaptopDiagramView.DropTarget(new Point(380, 160), LeftPorts, null);
        var front = LaptopDiagramView.DropTarget(new Point(200, 270), LeftPorts, null);

        Assert.Equal((PortEdge.Rear, 0), rear);
        Assert.Equal((PortEdge.Right, 0), right);
        Assert.Equal((PortEdge.Front, 0), front);
    }

    private static Point Midpoint(PortDiagramItem[] items, string first, string second)
    {
        var a = LaptopDiagramView.PortCenter(items, items.Single(item => item.Id == first));
        var b = LaptopDiagramView.PortCenter(items, items.Single(item => item.Id == second));
        return new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    private static PortDiagramItem Item(string id, PortEdge edge, int order) =>
        new(id, id, "usb-a", edge, order, PortCalibrationState.Uncalibrated, "未校準");
}
