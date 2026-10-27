namespace AudioSwitch.Core;

/// <summary>The device tree shown by the panel and settings. Roots are the laptop's ports and the fixed categories (built-in, Bluetooth, virtual, unidentified).</summary>
public sealed class TopologyTree
{
    public required IReadOnlyList<TopologyRoot> Roots { get; init; }
    public required IReadOnlyList<TopologyEndpoint> Endpoints { get; init; }
    public required IReadOnlyList<TopologyDisplayGroup> DisplayGroups { get; init; }
}

/// <summary>A root of the tree: a user-defined port or a fixed category. Id is stable; Name is the text shown.</summary>
public sealed class TopologyRoot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Source { get; init; }
    public List<TopologyNode> Children { get; } = [];
}

/// <summary>A node under a root: a dock or hub, a USB device, a screen or an endpoint (see Kind), nested by physical connection.</summary>
public sealed class TopologyNode
{
    public required string Name { get; init; }
    public string Kind { get; init; } = "device";
    public string? GroupKey { get; init; }
    public string? EndpointId { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> PhysicalDeviceLocationRaw { get; init; } = [];
    /// <summary>For a dock or hub, the VID/PID of its topmost hub chips; a user-given name is stored under this key.</summary>
    public string? DockKey { get; init; }
    public List<TopologyNode> Children { get; } = [];
}

/// <summary>Where an endpoint was placed: its category (A USB, B screen audio, C built-in or 3.5mm, D other), its port and its USB device group.</summary>
public sealed record TopologyEndpoint(
    string EndpointId,
    string? Name,
    string Flow,
    string Category,
    string? InterfaceId,
    string? GroupKey);

/// <summary>Where a display path was placed: its port and, for a USB display device, the USB device it groups with.</summary>
public sealed record TopologyDisplayGroup(string PathId, string? InterfaceId, string? GroupKey);
