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

public enum PortCalibrationState
{
    Uncalibrated,
    Partial,
    Calibrated,
    Automatic
}

public sealed record PortDiagramItem(string Id, string Name, string Type, PortEdge Edge, int Order, PortCalibrationState State, string StatusText)
{
    public string TypeLabel => PortDiagramViewModel.ConnectorLabel(Type);
    public string ShortStatus => State switch
    {
        PortCalibrationState.Calibrated => "已校準",
        PortCalibrationState.Partial => "缺部分路徑",
        PortCalibrationState.Automatic => "自動對應",
        _ => "未校準"
    };
    public string Description => Name.Contains(TypeLabel, StringComparison.OrdinalIgnoreCase)
        ? $"{Name}：{StatusText}"
        : $"{Name}（{TypeLabel}）：{StatusText}";
}

public sealed class PortDiagramViewModel
{
    private readonly PortTable _portTable;

    public ObservableCollection<PortDefinition> Interfaces { get; } = [];
    public bool IsEnabled => _portTable.IsEnabled;
    public string? Warning => _portTable.Warning;

    public PortDiagramViewModel(PortTable portTable, MachineIdentity? machine = null)
    {
        _portTable = portTable;
        if (machine is not null && _portTable.IsEnabled) _portTable.BindMachine(machine);
        if (_portTable.IsEnabled) ApplyAutoNames(_portTable.Interfaces.ToDictionary(port => port.Id, IsLegacyAutoName));
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
            Name = TypeName(type)
        };
        _portTable.AddInterface(port);
        oldAutoNames[port.Id] = true;
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
        var oldAutoNames = CaptureAutoNames();
        var removed = _portTable.RemoveInterface(interfaceId);
        ApplyAutoNames(oldAutoNames);
        Reload();
        return removed;
    }

    public IReadOnlyList<PortDefinition> GetEdgeInterfaces(PortEdge edge)
    {
        var edgeName = EdgeName(edge);
        return _portTable.Interfaces.Where(port => string.Equals(port.Edge ?? "left", edgeName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(port => port.Order ?? int.MaxValue).ToArray();
    }

    public IReadOnlyList<PortDiagramItem> GetItems() =>
        Enum.GetValues<PortEdge>().SelectMany(edge => GetEdgeInterfaces(edge).Select((port, index) =>
            new PortDiagramItem(port.Id, port.Name, port.Type, edge, index, GetCalibrationState(port), GetCalibrationStatus(port)))).ToArray();

    public PortCalibrationState GetCalibrationState(PortDefinition port)
    {
        if (port.Type == "audio-jack") return PortCalibrationState.Automatic;
        if (port.Type is "hdmi" or "displayport")
            return port.DisplayConnectors.Count > 0 ? PortCalibrationState.Calibrated : PortCalibrationState.Uncalibrated;
        var transports = CalibratedTransports(port);
        if (transports.Count == 0)
            return port.DisplayConnectors.Count > 0 ? PortCalibrationState.Partial : PortCalibrationState.Uncalibrated;
        return ExpectedTransports(port).All(transports.Contains) ? PortCalibrationState.Calibrated : PortCalibrationState.Partial;
    }

    public string GetCalibrationStatus(PortDefinition port)
    {
        if (port.Type == "audio-jack") return "自動對應，不需校準";
        if (port.Type is "hdmi" or "displayport")
            return port.DisplayConnectors.Count > 0 ? "已校準（顯示輸出）" : "未校準";

        var transports = CalibratedTransports(port);
        if (transports.Count == 0) return port.DisplayConnectors.Count > 0 ? "只校準了顯示輸出，缺 USB 路徑" : "未校準";
        var expected = ExpectedTransports(port);
        var present = expected.Where(transports.Contains).ToArray();
        var unknown = transports.Where(transport => !expected.Contains(transport, StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        var details = present.Concat(unknown).Concat(port.DisplayConnectors.Count > 0 ? ["顯示輸出"] : Array.Empty<string>()).ToArray();
        var missing = expected.Where(transport => !transports.Contains(transport)).ToArray();
        return missing.Length == 0
            ? $"已校準（{string.Join("、", details)}）"
            : $"缺部分路徑（{string.Join("、", missing.Select(item => "缺 " + item))}）";
    }

    public static PortEdge ParseEdge(string? edge) => edge switch
    {
        "right" => PortEdge.Right,
        "rear" => PortEdge.Rear,
        "front" => PortEdge.Front,
        _ => PortEdge.Left
    };

    public static string EdgeLabel(PortEdge edge) => edge switch
    {
        PortEdge.Left => "左側",
        PortEdge.Right => "右側",
        PortEdge.Rear => "後側",
        PortEdge.Front => "前側",
        _ => throw new ArgumentOutOfRangeException(nameof(edge))
    };

    public static string ConnectorLabel(string type) => type switch
    {
        "usb-a" => "USB-A",
        "usb-c" => "USB-C",
        "hdmi" => "HDMI",
        "displayport" => "DisplayPort",
        "audio-jack" => "3.5mm",
        _ => type
    };

    public static string ConnectorLabel(PortConnectorType type) => ConnectorLabel(TypeName(type));

    /// <summary>Suggested names for every port; ports of the same type on the same edge get distinct position words.</summary>
    public static IReadOnlyDictionary<string, string> SuggestNames(IEnumerable<PortDefinition> ports)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in ports.GroupBy(port => (Edge: port.Edge ?? "left", port.Type)))
        {
            var ordered = group.OrderBy(port => port.Order ?? int.MaxValue).ToArray();
            var side = EdgeLabel(ParseEdge(group.Key.Edge));
            var connector = ConnectorLabel(group.Key.Type);
            for (var index = 0; index < ordered.Length; index++)
            {
                var suffix = PositionSuffix(group.Key.Edge, index, ordered.Length);
                names[ordered[index].Id] = suffix is null ? $"{side} {connector}" : $"{side} {connector}（{suffix}）";
            }
        }
        return names;
    }

    private static string? PositionSuffix(string edge, int index, int count)
    {
        var vertical = edge is "left" or "right";
        return count switch
        {
            1 => null,
            2 => vertical ? (index == 0 ? "後" : "前") : (index == 0 ? "左" : "右"),
            3 => vertical ? new[] { "後", "中", "前" }[index] : new[] { "左", "中", "右" }[index],
            _ => $"第 {index + 1} 個"
        };
    }

    // Names written by earlier versions: "左側 HDMI（前）" from edge and order only, which could repeat.
    private static bool IsLegacyAutoName(PortDefinition port)
    {
        var edge = port.Edge ?? "left";
        var legacy = $"{EdgeLabel(ParseEdge(edge))} {ConnectorLabel(port.Type)}（{PositionName(edge, port.Order ?? 0)}）";
        return string.IsNullOrWhiteSpace(port.Name) || string.Equals(port.Name, legacy, StringComparison.Ordinal);
    }

    private Dictionary<string, bool> CaptureAutoNames()
    {
        var suggested = SuggestNames(_portTable.Interfaces);
        return _portTable.Interfaces.ToDictionary(port => port.Id,
            port => string.Equals(port.Name, suggested[port.Id], StringComparison.Ordinal) || IsLegacyAutoName(port));
    }

    private void ApplyAutoNames(IReadOnlyDictionary<string, bool> autoNamed)
    {
        var suggested = SuggestNames(_portTable.Interfaces);
        foreach (var port in _portTable.Interfaces)
        {
            if (string.IsNullOrWhiteSpace(port.Name) || autoNamed.TryGetValue(port.Id, out var isAutoName) && isAutoName)
                port.Name = suggested[port.Id];
            port.Position = PositionName(port.Edge ?? "left", port.Order ?? 0);
        }
    }

    private void Reload()
    {
        Interfaces.Clear();
        foreach (var port in _portTable.Interfaces.OrderBy(port => port.Edge, StringComparer.Ordinal).ThenBy(port => port.Order))
            Interfaces.Add(port);
    }

    private static HashSet<string> CalibratedTransports(PortDefinition port) => port.CalibrationSources
        .Where(source => !string.IsNullOrWhiteSpace(source.Transport))
        .Select(source => source.Transport!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string[] ExpectedTransports(PortDefinition port) =>
        port.Type == "usb-c" ? ["USB 2", "USB 3", "PCIe 隧道"] : ["USB 2", "USB 3"];

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
