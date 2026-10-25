using System.Collections.ObjectModel;
using AudioSwitch.Core;

namespace AudioSwitch.Presentation;

public enum PortEdge
{
    Left,
    Right,
    Rear,
    Front
}

public enum PortConnectorType
{
    UsbA,
    UsbC,
    Hdmi,
    AudioJack
}

public sealed class PortDiagramViewModel
{
    private readonly PortTable _portTable;

    public ObservableCollection<PortDefinition> Interfaces { get; } = [];

    public PortDiagramViewModel(PortTable portTable, MachineIdentity? machine = null)
    {
        _portTable = portTable;
        if (machine is not null && _portTable.IsEnabled) _portTable.BindMachine(machine);
        Reload();
    }

    public PortDefinition AddInterface(PortConnectorType type, PortEdge edge, int order)
    {
        var oldAutoNames = CaptureAutoNames();
        var edgeName = EdgeName(edge);
        var port = new PortDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = TypeName(type),
            Edge = edgeName,
            Order = order,
            Position = PositionName(edgeName, order),
            Source = "user",
            PositionSource = "user",
            Name = SuggestName(TypeName(type), edgeName, order)
        };
        _portTable.AddInterface(port);
        ApplyAutoNames(oldAutoNames);
        Reload();
        return port;
    }

    public void RenameInterface(string interfaceId, string name)
    {
        _portTable.RenameInterface(interfaceId, name);
        Reload();
    }

    public void MoveInterface(string interfaceId, PortEdge edge, int order)
    {
        var oldAutoNames = CaptureAutoNames();
        _portTable.MoveInterface(interfaceId, EdgeName(edge), order);
        ApplyAutoNames(oldAutoNames);
        Reload();
    }

    public bool DeleteInterface(string interfaceId)
    {
        var removed = _portTable.RemoveInterface(interfaceId);
        Reload();
        return removed;
    }

    public string GetCalibrationStatus(PortDefinition port)
    {
        if (port.Type == "audio-jack") return "KS 自動對應";
        if (port.Type is "hdmi" or "displayport")
            return port.DisplayConnectors.Count > 0 ? "已校準（顯示輸出）" : "未校準";

        var transports = port.CalibrationSources.Where(source => !string.IsNullOrWhiteSpace(source.Transport))
            .Select(source => source.Transport!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (transports.Count == 0) return "未校準";
        var expected = port.Type == "usb-c" ? new[] { "USB 2", "USB 3", "PCIe 隧道" } : new[] { "USB 2", "USB 3" };
        var present = expected.Where(transports.Contains).ToArray();
        var unknown = transports.Where(transport => !expected.Contains(transport, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        var details = present.Concat(unknown).ToArray();
        var missing = expected.Where(transport => !transports.Contains(transport)).ToArray();
        return missing.Length == 0
            ? $"已校準（{string.Join("、", details)}）"
            : $"缺部分路徑（{string.Join("、", missing.Select(item => "缺 " + item))}）";
    }

    public static string SuggestName(string type, string edge, int order)
    {
        var side = edge switch { "left" => "左側", "right" => "右側", "rear" => "後側", "front" => "前側", _ => "接口" };
        var connector = type switch { "usb-a" => "USB-A", "usb-c" => "USB-C", "hdmi" => "HDMI", "displayport" => "DisplayPort", "audio-jack" => "3.5mm", _ => type };
        var position = PositionName(edge, order);
        return $"{side} {connector}（{position}）";
    }

    private Dictionary<string, bool> CaptureAutoNames() => _portTable.Interfaces.ToDictionary(port => port.Id,
        port => string.Equals(port.Name, SuggestName(port.Type, port.Edge ?? "left", port.Order ?? 0), StringComparison.Ordinal));

    private void ApplyAutoNames(IReadOnlyDictionary<string, bool> oldAutoNames)
    {
        foreach (var port in _portTable.Interfaces)
        {
            if (string.IsNullOrWhiteSpace(port.Name) || oldAutoNames.TryGetValue(port.Id, out var isAutoName) && isAutoName)
                port.Name = SuggestName(port.Type, port.Edge ?? "left", port.Order ?? 0);
            port.Position = PositionName(port.Edge ?? "left", port.Order ?? 0);
        }
    }

    private void Reload()
    {
        Interfaces.Clear();
        foreach (var port in _portTable.Interfaces.OrderBy(port => port.Edge, StringComparer.Ordinal).ThenBy(port => port.Order))
            Interfaces.Add(port);
    }

    private static string TypeName(PortConnectorType type) => type switch
    {
        PortConnectorType.UsbA => "usb-a",
        PortConnectorType.UsbC => "usb-c",
        PortConnectorType.Hdmi => "hdmi",
        PortConnectorType.AudioJack => "audio-jack",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static string EdgeName(PortEdge edge) => edge switch
    {
        PortEdge.Left => "left",
        PortEdge.Right => "right",
        PortEdge.Rear => "rear",
        PortEdge.Front => "front",
        _ => throw new ArgumentOutOfRangeException(nameof(edge))
    };

    private static string PositionName(string edge, int order) => edge is "left" or "right"
        ? order == 0 ? "後" : "前"
        : order == 0 ? "左" : "右";
}
