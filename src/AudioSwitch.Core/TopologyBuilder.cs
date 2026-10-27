namespace AudioSwitch.Core;

/// <summary>Builds the device tree from a snapshot and the port table. Each active endpoint is classified by its USB path, screen sink or jack; USB endpoints are grouped by device, everything behind hubs on one port forms one dock node, and screens are placed by their recorded connector, their USB adapter or the single HDMI port.</summary>
public static class TopologyBuilder
{
    private static readonly HashSet<int> PhysicalJackConnectionTypes = [1, 2, 11];
    private static readonly HashSet<int> PhysicalJackPortConnections = [0, 2];
    private static readonly string[] RootCategories = ["內建", "藍牙／無線", "虛擬", "未辨識"];
    // Root IDs and the default dock name are stable keys; their visible text goes through Loc.T.
    public const string UnconfiguredUsbRootId = "USB（未設定接口）";
    public const string DefaultDockName = "Dock／Hub";
    private const string UnknownSource = "未知";

    public static TopologyTree Build(Snapshot snapshot, PortTable portTable, IReadOnlyDictionary<string, string>? dockNames = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(portTable);

        var roots = portTable.IsEnabled
            ? portTable.Interfaces.Select(port => new TopologyRoot { Id = port.Id, Name = port.Name, Source = port.PositionSource ?? UnknownSource }).ToList()
            : [];
        roots.Add(new TopologyRoot { Id = UnconfiguredUsbRootId, Name = Loc.T(UnconfiguredUsbRootId), Source = UnknownSource });
        roots.AddRange(RootCategories.Select(category => new TopologyRoot { Id = category, Name = Loc.T(category), Source = UnknownSource }));
        var rootsById = roots.ToDictionary(root => root.Id, StringComparer.Ordinal);
        var nodesById = snapshot.Devnodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
        var chains = new Dictionary<string, List<DeviceNode>>(StringComparer.Ordinal);
        var endpoints = new List<TopologyEndpoint>();
        var endpointRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        var usbGroups = new Dictionary<(string Root, string Device), TopologyNode>();

        // Hubs are not shown on their own: everything behind the hubs on one port forms a single dock node, because one
        // connector carries one dock (its USB 2 and USB 3 hubs included). Without a port, each top hub is its own node.
        var usbParentIds = snapshot.Devnodes.Where(IsUsbDeviceNode).Select(node => node.ParentId)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsHub(DeviceNode node) => IsUsbDeviceNode(node) && usbParentIds.Contains(node.InstanceId);
        var topHubs = snapshot.Devnodes.Where(node => IsHub(node) &&
            !(node.ParentId is not null && nodesById.TryGetValue(node.ParentId, out var parent) && IsUsbDeviceNode(parent))).ToArray();
        var docks = new Dictionary<string, TopologyNode>(StringComparer.OrdinalIgnoreCase);
        var portIds = portTable.IsEnabled ? portTable.Interfaces.Select(port => port.Id).ToHashSet(StringComparer.Ordinal) : [];
        TopologyNode DockFor(TopologyRoot root, DeviceNode topHub)
        {
            var isPort = portIds.Contains(root.Id);
            var key = isPort ? root.Id : root.Id + "|" + topHub.InstanceId;
            if (docks.TryGetValue(key, out var dock)) return dock;
            var members = isPort ? topHubs.Where(hub => UsbInterfaceId([hub], portTable) == root.Id).Append(topHub) : [topHub];
            var dockKey = string.Join("+", members.Select(HardwareKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            var name = dockNames is not null && dockNames.TryGetValue(dockKey, out var stored) && !string.IsNullOrWhiteSpace(stored)
                ? stored
                : Loc.T(DefaultDockName);
            dock = new TopologyNode { Name = name, Kind = "dock-hub", GroupKey = topHub.InstanceId, DockKey = dockKey };
            root.Children.Add(dock);
            docks[key] = dock;
            return dock;
        }
        // The chain runs from the device up to the computer, so the last hub in it is the topmost one.
        List<TopologyNode> ContainerFor(TopologyRoot root, IEnumerable<DeviceNode> chain) =>
            chain.LastOrDefault(IsHub) is { } topHub ? DockFor(root, topHub).Children : root.Children;
        var pendingScreenAudio = new Dictionary<string, List<(string Root, TopologyNode Leaf)>>(StringComparer.Ordinal);

        foreach (var endpoint in snapshot.Endpoints.Where(IsActive))
        {
            var chain = BuildChain(endpoint.DevnodeId, nodesById);
            chains[endpoint.EndpointId] = chain;
            var endpointJacks = snapshot.Jacks.Where(jack => jack.EndpointId.Equals(endpoint.EndpointId, StringComparison.OrdinalIgnoreCase)).ToArray();
            var classification = Classify(endpoint, chain, endpointJacks, snapshot.DisplayPaths, portTable);
            var rootId = ResolveRoot(classification.Category, classification.InterfaceId, chain);
            var root = rootsById[rootId];
            endpointRoots[endpoint.EndpointId] = rootId;

            var usbDevices = chain.Where(IsUsbDeviceNode).Reverse().ToArray();
            var endpointUsbDevice = usbDevices.LastOrDefault();
            var groupKey = endpointUsbDevice?.InstanceId;
            var leaf = new TopologyNode
            {
                Name = endpoint.FriendlyName ?? Loc.T("（無名稱）"),
                Kind = "endpoint",
                EndpointId = endpoint.EndpointId,
                Category = classification.Category,
                PhysicalDeviceLocationRaw = chain.Where(node => node.PhysicalDeviceLocationRaw is { Length: > 0 })
                    .Select(node => $"{node.InstanceId}: {Convert.ToHexString(node.PhysicalDeviceLocationRaw!)}").ToArray()
            };

            if (endpointUsbDevice is not null)
            {
                var key = (rootId, endpointUsbDevice.InstanceId.ToUpperInvariant());
                if (!usbGroups.TryGetValue(key, out var group))
                {
                    group = CreateUsbGroup(endpointUsbDevice, snapshot.Devnodes);
                    ContainerFor(root, chain).Add(group);
                    usbGroups[key] = group;
                }
                group.Children.Add(leaf);
            }
            else if (classification.SinkPathId is { } sinkPathId)
            {
                // Screen audio goes under its screen, which is placed with the display paths below.
                if (!pendingScreenAudio.TryGetValue(sinkPathId, out var leaves)) pendingScreenAudio[sinkPathId] = leaves = [];
                leaves.Add((rootId, leaf));
            }
            else
            {
                root.Children.Add(leaf);
            }

            endpoints.Add(new TopologyEndpoint(endpoint.EndpointId, endpoint.FriendlyName, endpoint.Flow,
                classification.Category, classification.InterfaceId, groupKey));
        }

        var displayGroups = new List<TopologyDisplayGroup>();
        var screenNumbers = ScreenNumbering.Number(snapshot.DisplayPaths
            .Where(path => path.SourceName is not null && path.SourcePosition is not null)
            .Select(path => (path.SourceName!, JsonValue.Integer(path.SourcePosition!, "x") ?? 0, JsonValue.Integer(path.SourcePosition!, "y") ?? 0)));
        foreach (var path in snapshot.DisplayPaths)
        {
            var usb = FindUsbAdapterNode(path, snapshot.Devnodes, nodesById);
            string? groupKey = usb?.InstanceId;
            string? interfaceId = null;
            if (usb is not null)
            {
                var relatedEndpoint = snapshot.Endpoints.FirstOrDefault(endpoint =>
                    chains.TryGetValue(endpoint.EndpointId, out var chain) &&
                    chain.Any(node => node.InstanceId.Equals(usb.InstanceId, StringComparison.OrdinalIgnoreCase)));
                if (relatedEndpoint is not null && endpointRoots.TryGetValue(relatedEndpoint.EndpointId, out var endpointRoot))
                {
                    interfaceId = endpointRoot;
                }
            }

            interfaceId ??= DisplayInterfaceId(path, snapshot.DisplayPaths, portTable);
            // A USB display adapter without audio, for example on a dock, is placed through its own USB path.
            if (interfaceId is null && usb is not null) interfaceId = UsbInterfaceId([usb], portTable);
            displayGroups.Add(new TopologyDisplayGroup(path.PathId, interfaceId, groupKey));

            if (interfaceId is null || !rootsById.TryGetValue(interfaceId, out var root))
            {
                continue;
            }

            var screen = new TopologyNode { Name = ScreenNumbering.Label(path, snapshot.DisplayPaths, screenNumbers), Kind = "display", GroupKey = groupKey };
            if (pendingScreenAudio.Remove(path.PathId, out var screenAudio))
            {
                foreach (var (audioRoot, leaf) in screenAudio)
                {
                    if (audioRoot == interfaceId) screen.Children.Add(leaf);
                    else rootsById[audioRoot].Children.Add(leaf);
                }
            }

            if (groupKey is not null && usbGroups.TryGetValue((interfaceId, groupKey.ToUpperInvariant()), out var usbGroup))
            {
                // A USB display device that also has audio keeps its screen and endpoints together.
                usbGroup.Children.Add(screen);
            }
            else if (usb is not null)
            {
                ContainerFor(root, BuildChain(usb.InstanceId, nodesById)).Add(screen);
            }
            else
            {
                // A screen on the host GPU joins the dock on its port, if one is plugged in there.
                var portHub = topHubs.FirstOrDefault(hub => UsbInterfaceId([hub], portTable) == interfaceId);
                (portHub is null ? root.Children : DockFor(root, portHub).Children).Add(screen);
            }
        }

        foreach (var (audioRoot, leaf) in pendingScreenAudio.Values.SelectMany(leaves => leaves))
        {
            rootsById[audioRoot].Children.Add(leaf);
        }

        var nonEmptyRoots = roots.Where(root => root.Children.Count > 0).ToArray();
        return new TopologyTree { Roots = nonEmptyRoots, Endpoints = endpoints, DisplayGroups = displayGroups };
    }

    private static bool IsActive(AudioEndpoint endpoint) =>
        endpoint.State.Equals("active", StringComparison.OrdinalIgnoreCase) ||
        endpoint.State.EndsWith(".active", StringComparison.OrdinalIgnoreCase);

    private static bool IsUsbDeviceNode(DeviceNode node) =>
        node.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) &&
        !node.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase) &&
        !node.InstanceId.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase);

    private static TopologyNode CreateUsbGroup(DeviceNode usb, IReadOnlyList<DeviceNode> nodes)
    {
        var isHub = nodes.Any(node => node.ParentId?.Equals(usb.InstanceId, StringComparison.OrdinalIgnoreCase) == true && IsUsbDeviceNode(node));
        var functionName = nodes.FirstOrDefault(node =>
            node.ParentId?.Equals(usb.InstanceId, StringComparison.OrdinalIgnoreCase) == true &&
            !IsUsbDeviceNode(node) && !string.IsNullOrWhiteSpace(node.Name) &&
            !node.Name.StartsWith("36fc", StringComparison.OrdinalIgnoreCase) &&
            !node.Name.StartsWith("4d36", StringComparison.OrdinalIgnoreCase))?.Name;
        var label = isHub
            ? Loc.T(DefaultDockName)
            : functionName ?? (usb.Name is not null && !usb.Name.StartsWith("36fc", StringComparison.OrdinalIgnoreCase) &&
                !usb.Name.StartsWith("4d36", StringComparison.OrdinalIgnoreCase) ? usb.Name : Loc.T("USB 裝置"));
        return new TopologyNode { Name = label, Kind = isHub ? "dock-hub" : "device", GroupKey = usb.InstanceId };
    }

    /// <summary>"VID_xxxx&amp;PID_yyyy" from a USB instance ID; the model of a hub chip, without its serial number.</summary>
    private static string HardwareKey(DeviceNode node)
    {
        var parts = node.InstanceId.Split('\\');
        return (parts.Length > 1 ? parts[1] : node.InstanceId).ToUpperInvariant();
    }

    private static List<DeviceNode> BuildChain(string? startId, IReadOnlyDictionary<string, DeviceNode> nodes)
    {
        var chain = new List<DeviceNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = startId;
        while (!string.IsNullOrWhiteSpace(current) && seen.Add(current) && nodes.TryGetValue(current, out var node))
        {
            chain.Add(node);
            current = node.ParentId;
        }
        return chain;
    }

    private static Classification Classify(AudioEndpoint endpoint, IReadOnlyList<DeviceNode> chain,
        IReadOnlyList<JackRecord> jacks, IReadOnlyList<DisplayPath> displayPaths, PortTable ports)
    {
        var effectiveChain = chain.Where(node => !node.InstanceId.StartsWith("SWD\\MMDEVAPI\\", StringComparison.OrdinalIgnoreCase)).ToArray();
        var upperIds = effectiveChain.Select(node => node.InstanceId.ToUpperInvariant()).ToArray();
        if (upperIds.Any(id => id.Contains("BTH", StringComparison.Ordinal) || id.Contains("BLUETOOTH", StringComparison.Ordinal)))
        {
            return new Classification("D", null);
        }

        var usbNodes = effectiveChain.Where(node => node.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (usbNodes.Length > 0)
        {
            return new Classification("A", UsbInterfaceId(usbNodes, ports));
        }

        var sinkPath = jacks.Select(jack => MatchSink(jack.Sink, displayPaths)).FirstOrDefault(path => path is not null);
        if (sinkPath is not null)
        {
            return new Classification("B", DisplayInterfaceId(sinkPath, displayPaths, ports), sinkPath.PathId);
        }

        if (jacks.Any(jack => jack.Jack.Count > 0) || upperIds.Any(id => id.StartsWith("INTELAUDIO\\", StringComparison.Ordinal) || id.StartsWith("HDAUDIO\\", StringComparison.Ordinal)))
        {
            var physicalJack = jacks.Any(IsPhysicalJack);
            var jackPort = physicalJack && ports.IsEnabled ? ports.Interfaces.FirstOrDefault(port => port.Type == "audio-jack")?.Id : null;
            return new Classification("C", jackPort);
        }

        return new Classification("D", null);
    }

    private static string ResolveRoot(string category, string? interfaceId, IReadOnlyList<DeviceNode> chain)
    {
        if (interfaceId is not null)
        {
            return interfaceId;
        }
        if (category == "C")
        {
            return "內建";
        }
        if (category == "D")
        {
            var ids = chain.Where(node => !node.InstanceId.StartsWith("SWD\\MMDEVAPI\\", StringComparison.OrdinalIgnoreCase))
                .Select(node => node.InstanceId.ToUpperInvariant()).ToArray();
            if (ids.Any(id => id.Contains("BTH", StringComparison.Ordinal) || id.Contains("BLUETOOTH", StringComparison.Ordinal)))
            {
                return "藍牙／無線";
            }
            if (ids.Any(id => id.StartsWith("ROOT\\MEDIA", StringComparison.Ordinal) || id.StartsWith("SWD\\", StringComparison.Ordinal) || id.StartsWith("ROOT\\SYSTEM", StringComparison.Ordinal)))
            {
                return "虛擬";
            }
        }
        return category == "A" ? UnconfiguredUsbRootId : "未辨識";
    }

    /// <summary>The EPxJackConnection a driver reports for a jack, if any: 0 a jack, 1 integrated, 2 both, 3 unknown.</summary>
    public static int? PortConnection(JackRecord jack) => JsonValue.Integer(jack.Jack, "port_connection");

    public static bool IsPhysicalJack(JackRecord jack)
    {
        var connection = JsonValue.Integer(jack.Jack, "connection_type");
        var portConnection = JsonValue.Integer(jack.Jack, "port_connection");
        return connection.HasValue && portConnection.HasValue &&
            PhysicalJackConnectionTypes.Contains(connection.Value) && PhysicalJackPortConnections.Contains(portConnection.Value);
    }

    private static DisplayPath? MatchSink(Dictionary<string, System.Text.Json.JsonElement>? sink, IReadOnlyList<DisplayPath> paths)
    {
        if (sink is null || sink.Count == 0)
        {
            return null;
        }

        var targetId = JsonValue.Integer(sink, "target_id");
        var manufacturer = JsonValue.Integer(sink, "manufacturer_id");
        var product = JsonValue.Integer(sink, "product_id");
        var adapterKey = sink.ContainsKey("port_id") ? "port_id" : "adapter_luid";
        if (sink.TryGetValue(adapterKey, out var adapterValue))
        {
            var adapterMatches = paths.Where(path => path.AdapterLuid is not null && JsonValue.StructuredEquals(adapterValue, path.AdapterLuid) &&
                (!targetId.HasValue || JsonValue.Integer(path.Properties, "target_id") == targetId)).ToArray();
            if (adapterMatches.Length > 0)
            {
                return adapterMatches.Length == 1 ? adapterMatches[0] : null;
            }
        }
        if (manufacturer.HasValue && product.HasValue)
        {
            var edidMatches = paths.Where(path => path.EdidManufacturerId == manufacturer && path.EdidProductCodeId == product).ToArray();
            return edidMatches.Length == 1 ? edidMatches[0] : null;
        }
        return null;
    }

    private static string? UsbInterfaceId(IEnumerable<DeviceNode> usbNodes, PortTable ports)
    {
        var matches = usbNodes.SelectMany(node => node.LocationPaths)
            .Select(ports.FindForLocationPath)
            .Where(port => port is not null)
            .Select(port => port!.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>The port a screen is on: a recorded display connector, or else the laptop's single HDMI port.</summary>
    private static string? DisplayInterfaceId(DisplayPath path, IReadOnlyList<DisplayPath> allPaths, PortTable ports)
    {
        var matches = CalibratedDisplayPorts(path, ports);
        if (matches.Length > 0) return matches.Length == 1 ? matches[0] : null;

        // The laptop's own HDMI connector reports HDMI on the computer's GPU. Screens through a dock or a USB-C adapter report
        // DisplayPort, and USB display devices also report HDMI but sit on a USB adapter, so they are left out. One connector
        // carries one screen: if several uncalibrated screens qualify, the evidence does not say which one it is.
        var hdmiPorts = ports.IsEnabled ? ports.Interfaces.Where(port => port.Type == "hdmi").Select(port => port.Id).ToArray() : [];
        return hdmiPorts.Length == 1 && IsHdmiOnHostGpu(path) &&
            allPaths.Count(other => IsHdmiOnHostGpu(other) && CalibratedDisplayPorts(other, ports).Length == 0) == 1
                ? hdmiPorts[0]
                : null;
    }

    private static string[] CalibratedDisplayPorts(DisplayPath path, PortTable ports)
    {
        var target = JsonValue.Integer(path.Properties, "target_id");
        return (ports.IsEnabled ? ports.Interfaces : []).Where(port => port.DisplayConnectors.Any(connector =>
            !string.IsNullOrWhiteSpace(path.AdapterName) && !string.IsNullOrWhiteSpace(connector.AdapterDevicePath) &&
            string.Equals(path.AdapterName, connector.AdapterDevicePath, StringComparison.OrdinalIgnoreCase) &&
            target.HasValue && connector.TargetId == target &&
            path.ConnectorInstance.HasValue && connector.ConnectorInstance == path.ConnectorInstance &&
            path.OutputTechnology.HasValue && connector.OutputTechnology == path.OutputTechnology)).Select(port => port.Id).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI.</summary>
    public const long HdmiOutputTechnology = 5;

    private static bool IsHdmiOnHostGpu(DisplayPath path) =>
        path.OutputTechnology == HdmiOutputTechnology && path.AdapterInstanceId?.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when the only HDMI port on the drawing is matched to screens automatically, without calibration.</summary>
    public static bool IsAutomaticHdmiPort(PortTable ports, PortDefinition port) =>
        port.Type == "hdmi" && ports.Interfaces.Count(item => item.Type == "hdmi") == 1;

    private static DeviceNode? FindUsbAdapterNode(DisplayPath path, IReadOnlyList<DeviceNode> allNodes,
        IReadOnlyDictionary<string, DeviceNode> nodesById)
    {
        DeviceNode? node = null;
        if (path.AdapterInstanceId?.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) == true)
        {
            nodesById.TryGetValue(path.AdapterInstanceId, out node);
        }
        if (node is not null && node.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase) && node.ParentId is not null)
        {
            nodesById.TryGetValue(node.ParentId, out node);
        }
        if (node is not null)
        {
            return node;
        }

        var adapterPath = (path.AdapterName ?? string.Empty).Replace('#', '\\');
        var candidates = allNodes.Where(candidate => candidate.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) &&
            !candidate.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.InstanceId.Split('\\').Length > 1 &&
                adapterPath.Contains("USB\\" + candidate.InstanceId.Split('\\')[1], StringComparison.OrdinalIgnoreCase)).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private sealed record Classification(string Category, string? InterfaceId, string? SinkPathId = null);
}
