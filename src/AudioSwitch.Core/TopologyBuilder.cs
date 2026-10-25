namespace AudioSwitch.Core;

public sealed class TopologyTree
{
    public required IReadOnlyList<TopologyRoot> Roots { get; init; }
    public required IReadOnlyList<TopologyEndpoint> Endpoints { get; init; }
    public required IReadOnlyList<TopologyDisplayGroup> DisplayGroups { get; init; }
}

public sealed class TopologyRoot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Source { get; init; }
    public List<TopologyNode> Children { get; } = [];
}

public sealed class TopologyNode
{
    public required string Name { get; init; }
    public string Kind { get; init; } = "device";
    public string? GroupKey { get; init; }
    public string? EndpointId { get; init; }
    public string? Category { get; init; }
    public List<TopologyNode> Children { get; } = [];
}

public sealed record TopologyEndpoint(
    string EndpointId,
    string? Name,
    string Flow,
    string Category,
    string? InterfaceId,
    string? GroupKey);

public sealed record TopologyDisplayGroup(string PathId, string? InterfaceId, string? GroupKey);

public static class TopologyBuilder
{
    private static readonly HashSet<int> PhysicalJackConnectionTypes = [1, 2, 11];
    private static readonly HashSet<int> PhysicalJackPortConnections = [0, 2];
    private static readonly string[] RootCategories = ["內建", "藍牙／無線", "虛擬", "未辨識"];

    public static TopologyTree Build(Snapshot snapshot, PortTable portTable)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(portTable);

        var roots = portTable.IsEnabled
            ? portTable.Interfaces.Select(port => new TopologyRoot { Id = port.Id, Name = port.Name, Source = port.PositionSource ?? "未知" }).ToList()
            : [];
        roots.Add(new TopologyRoot { Id = "USB（未設定接口）", Name = "USB（未設定接口）", Source = "未知" });
        roots.AddRange(RootCategories.Select(category => new TopologyRoot { Id = category, Name = category, Source = "未知" }));
        var rootsById = roots.ToDictionary(root => root.Id, StringComparer.Ordinal);
        var nodesById = snapshot.Devnodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
        var chains = new Dictionary<string, List<DeviceNode>>(StringComparer.Ordinal);
        var endpoints = new List<TopologyEndpoint>();
        var endpointRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        var usbGroups = new Dictionary<(string Root, string Device), TopologyNode>();

        foreach (var endpoint in snapshot.Endpoints.Where(IsActive))
        {
            var chain = BuildChain(endpoint.DevnodeId, nodesById);
            chains[endpoint.EndpointId] = chain;
            var endpointJacks = snapshot.Jacks.Where(jack => jack.EndpointId.Equals(endpoint.EndpointId, StringComparison.OrdinalIgnoreCase)).ToArray();
            var classification = Classify(endpoint, chain, endpointJacks, snapshot.DisplayPaths, portTable);
            var rootId = ResolveRoot(classification.Category, classification.InterfaceId, chain);
            var root = rootsById[rootId];
            endpointRoots[endpoint.EndpointId] = rootId;

            var usb = chain.FirstOrDefault(node => node.InstanceId.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) &&
                node.LocationPaths.Count > 0 && !node.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) ??
                chain.FirstOrDefault(node => node.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) &&
                    !node.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase));
            var groupKey = usb?.InstanceId;
            var leaf = new TopologyNode
            {
                Name = endpoint.FriendlyName ?? "（無名稱）",
                Kind = "endpoint",
                EndpointId = endpoint.EndpointId,
                Category = classification.Category
            };

            if (usb is not null)
            {
                var key = (rootId, usb.InstanceId.ToUpperInvariant());
                if (!usbGroups.TryGetValue(key, out var group))
                {
                    var functionName = snapshot.Devnodes.FirstOrDefault(node =>
                        node.ParentId?.Equals(usb.InstanceId, StringComparison.OrdinalIgnoreCase) == true &&
                        !string.IsNullOrWhiteSpace(node.Name) && !node.Name.StartsWith("36fc", StringComparison.OrdinalIgnoreCase) &&
                        !node.Name.StartsWith("4d36", StringComparison.OrdinalIgnoreCase))?.Name;
                    var label = functionName ?? (usb.Name is not null && !usb.Name.StartsWith("36fc", StringComparison.OrdinalIgnoreCase) &&
                        !usb.Name.StartsWith("4d36", StringComparison.OrdinalIgnoreCase) ? usb.Name : "USB 裝置");
                    group = new TopologyNode { Name = label, GroupKey = usb.InstanceId };
                    root.Children.Add(group);
                    usbGroups[key] = group;
                }
                group.Children.Add(leaf);
            }
            else
            {
                root.Children.Add(leaf);
            }

            endpoints.Add(new TopologyEndpoint(endpoint.EndpointId, endpoint.FriendlyName, endpoint.Flow,
                classification.Category, classification.InterfaceId, groupKey));
        }

        var displayGroups = new List<TopologyDisplayGroup>();
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

            interfaceId ??= DisplayInterfaceId(path, portTable);
            displayGroups.Add(new TopologyDisplayGroup(path.PathId, interfaceId, groupKey));

            if (interfaceId is null || !rootsById.TryGetValue(interfaceId, out var root))
            {
                continue;
            }

            TopologyNode? device = null;
            if (groupKey is not null)
            {
                device = root.Children.FirstOrDefault(child => child.GroupKey?.Equals(groupKey, StringComparison.OrdinalIgnoreCase) == true);
            }
            if (device is null)
            {
                device = new TopologyNode { Name = path.TargetName ?? "顯示器", GroupKey = groupKey };
                root.Children.Add(device);
            }
            device.Children.Add(new TopologyNode { Name = path.TargetName ?? "顯示器", Kind = "display" });
        }

        var nonEmptyRoots = roots.Where(root => root.Children.Count > 0).ToArray();
        return new TopologyTree { Roots = nonEmptyRoots, Endpoints = endpoints, DisplayGroups = displayGroups };
    }

    private static bool IsActive(AudioEndpoint endpoint) =>
        endpoint.State.Equals("active", StringComparison.OrdinalIgnoreCase) ||
        endpoint.State.EndsWith(".active", StringComparison.OrdinalIgnoreCase);

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
            var matches = usbNodes.SelectMany(node => node.LocationPaths)
                .Select(ports.FindForLocationPath)
                .Where(port => port is not null)
                .Select(port => port!.Id)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new Classification("A", matches.Length == 1 ? matches[0] : null);
        }

        var sinkPath = jacks.Select(jack => MatchSink(jack.Sink, displayPaths)).FirstOrDefault(path => path is not null);
        if (sinkPath is not null)
        {
            return new Classification("B", DisplayInterfaceId(sinkPath, ports));
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
            var ids = chain.Select(node => node.InstanceId.ToUpperInvariant()).ToArray();
            if (ids.Any(id => id.Contains("BTH", StringComparison.Ordinal) || id.Contains("BLUETOOTH", StringComparison.Ordinal)))
            {
                return "藍牙／無線";
            }
            if (ids.Any(id => id.StartsWith("ROOT\\MEDIA", StringComparison.Ordinal) || id.StartsWith("SWD\\", StringComparison.Ordinal) || id.StartsWith("ROOT\\SYSTEM", StringComparison.Ordinal)))
            {
                return "虛擬";
            }
        }
        return category == "A" ? "USB（未設定接口）" : "未辨識";
    }

    private static bool IsPhysicalJack(JackRecord jack)
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

    private static string? DisplayInterfaceId(DisplayPath path, PortTable ports)
    {
        var target = JsonValue.Integer(path.Properties, "target_id");
        var matches = (ports.IsEnabled ? ports.Interfaces : []).Where(port => port.DisplayConnectors.Any(connector =>
            !string.IsNullOrWhiteSpace(path.AdapterName) && !string.IsNullOrWhiteSpace(connector.AdapterDevicePath) &&
            string.Equals(path.AdapterName, connector.AdapterDevicePath, StringComparison.OrdinalIgnoreCase) &&
            target.HasValue && connector.TargetId == target &&
            path.ConnectorInstance.HasValue && connector.ConnectorInstance == path.ConnectorInstance &&
            path.OutputTechnology.HasValue && connector.OutputTechnology == path.OutputTechnology)).Select(port => port.Id).Distinct(StringComparer.Ordinal).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

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

    private sealed record Classification(string Category, string? InterfaceId);
}
