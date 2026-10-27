using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSwitch.Core;

/// <summary>aliases.json: the original Windows description of every endpoint AudioSwitch renamed, so its name can be restored.</summary>
public sealed class AliasStore
{
    private readonly Dictionary<string, string> _originalDescriptions;

    public AliasStore() : this(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) { }

    internal AliasStore(Dictionary<string, string> originalDescriptions) => _originalDescriptions = originalDescriptions;

    public IReadOnlyDictionary<string, string> OriginalDescriptions => _originalDescriptions;

    public void SetAlias(IEnumerable<string> endpointIds, IReadOnlyDictionary<string, string> currentDeviceDescriptions)
    {
        foreach (var endpointId in endpointIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_originalDescriptions.ContainsKey(endpointId) && currentDeviceDescriptions.TryGetValue(endpointId, out var original))
            {
                _originalDescriptions.Add(endpointId, original);
            }
        }
    }

    public IReadOnlyDictionary<string, string> ClearAlias(IEnumerable<string> endpointIds)
    {
        var restored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpointId in endpointIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_originalDescriptions.Remove(endpointId, out var original))
            {
                restored.Add(endpointId, original);
            }
        }
        return restored;
    }

    internal AliasStore Clone() => new(new Dictionary<string, string>(_originalDescriptions, StringComparer.OrdinalIgnoreCase));
}

/// <summary>The endpoints that share one name: the playback and recording endpoints of the same physical device.</summary>
public static class AliasEndpointGrouping
{
    public static IReadOnlyList<string> GetMembers(TopologyEndpoint selected, IEnumerable<TopologyEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(endpoints);

        if (!string.IsNullOrWhiteSpace(selected.GroupKey))
        {
            return endpoints.Where(endpoint => string.Equals(endpoint.GroupKey, selected.GroupKey, StringComparison.OrdinalIgnoreCase))
                .Select(endpoint => endpoint.EndpointId).ToArray();
        }

        if (selected.Category == "C" && !string.IsNullOrWhiteSpace(selected.InterfaceId))
        {
            return endpoints.Where(endpoint => endpoint.Category == "C" &&
                string.Equals(endpoint.InterfaceId, selected.InterfaceId, StringComparison.Ordinal))
                .Select(endpoint => endpoint.EndpointId).ToArray();
        }

        return [selected.EndpointId];
    }
}
