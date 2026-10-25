namespace AudioSwitch.Core;

public static class AttachmentDetector
{
    public static DeviceNode? FindCurrentPathAttachment(Snapshot snapshot, string endpointId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        var endpoint = snapshot.Endpoints.FirstOrDefault(candidate => candidate.EndpointId.Equals(endpointId, StringComparison.OrdinalIgnoreCase));
        if (endpoint?.DevnodeId is null)
        {
            return null;
        }

        var nodes = snapshot.Devnodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = endpoint.DevnodeId;
        DeviceNode? highestAttachment = null;
        while (seen.Add(currentId) && nodes.TryGetValue(currentId, out var current))
        {
            if (current.ParentId is not null && nodes.TryGetValue(current.ParentId, out var parent) && IsRootConnector(parent, current))
            {
                highestAttachment = current;
            }
            currentId = current.ParentId ?? string.Empty;
            if (currentId.Length == 0)
            {
                break;
            }
        }

        return highestAttachment;
    }

    public static IReadOnlyList<DeviceNode> Detect(Snapshot before, Snapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeById = before.Devnodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
        var candidates = new List<DeviceNode>();
        foreach (var node in after.Devnodes)
        {
            if (!beforeById.TryGetValue(node.InstanceId, out var prior))
            {
                candidates.Add(node);
            }
            else if (!prior.LocationPaths.SequenceEqual(node.LocationPaths, StringComparer.Ordinal))
            {
                candidates.Add(node);
            }
        }

        var candidateIds = candidates.Select(node => node.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(IsAttachment)
            .Where(node => string.IsNullOrWhiteSpace(node.ParentId) || !candidateIds.Contains(node.ParentId))
            .ToArray();
    }

    public static string Transport(DeviceNode node)
    {
        if (node.InstanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
        {
            return "PCIe 隧道";
        }

        var paths = string.Join(' ', node.LocationPaths).ToUpperInvariant();
        if (System.Text.RegularExpressions.Regex.IsMatch(paths, @"(?:^|[^A-Z0-9])SS\d+", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            return "USB 3";
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(paths, @"(?:^|[^A-Z0-9])HS\d+", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            return "USB 2";
        }

        return "USB 未知";
    }

    public static CalibrationResult Calibrate(string interfaceId, Snapshot before, Snapshot after, PortTable portTable)
    {
        ArgumentNullException.ThrowIfNull(portTable);
        return portTable.Calibrate(interfaceId, before, after);
    }

    private static bool IsAttachment(DeviceNode node)
    {
        if (node.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (node.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
        {
            return node.LocationPaths.Count > 0;
        }

        return node.InstanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) &&
               node.LocationPaths.Any(path => path.StartsWith("PCIROOT(", StringComparison.OrdinalIgnoreCase) &&
                   System.Text.RegularExpressions.Regex.Matches(path, @"#PCI\(", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count >= 2);
    }

    private static bool IsRootConnector(DeviceNode parent, DeviceNode child)
    {
        if (parent.InstanceId.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return parent.InstanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) &&
               child.InstanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) &&
               parent.LocationPaths.Any(path => IsPciLocationPath(path) && CountPciSegments(path) == 1) &&
               child.LocationPaths.Any(path => IsPciLocationPath(path) && CountPciSegments(path) >= 2);
    }

    private static bool IsPciLocationPath(string path) => path.StartsWith("PCIROOT(", StringComparison.OrdinalIgnoreCase);

    private static int CountPciSegments(string path) =>
        System.Text.RegularExpressions.Regex.Matches(path, @"#PCI\(", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
}
