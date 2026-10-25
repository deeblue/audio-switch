using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSwitch.Core;

public sealed class Snapshot
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("captured_at")] public string CapturedAt { get; set; } = string.Empty;
    [JsonPropertyName("machine")] public MachineIdentity Machine { get; set; } = new();
    [JsonPropertyName("endpoints")] public List<AudioEndpoint> Endpoints { get; set; } = [];
    [JsonPropertyName("devnodes")] public List<DeviceNode> Devnodes { get; set; } = [];
    [JsonPropertyName("display_paths")] public List<DisplayPath> DisplayPaths { get; set; } = [];
    [JsonPropertyName("jacks")] public List<JackRecord> Jacks { get; set; } = [];
}

public sealed class MachineIdentity
{
    [JsonPropertyName("manufacturer")] public string? Manufacturer { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("mtm")] public string? MtM { get; set; }
}

public sealed class AudioEndpoint
{
    [JsonPropertyName("endpoint_id")] public string EndpointId { get; set; } = string.Empty;
    [JsonPropertyName("flow")] public string Flow { get; set; } = string.Empty;
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("friendly_name")] public string? FriendlyName { get; set; }
    [JsonPropertyName("form_factor")] public int? FormFactor { get; set; }
    [JsonPropertyName("container_id")] public string? ContainerId { get; set; }
    [JsonPropertyName("stable_id")] public string? StableId { get; set; }
    [JsonPropertyName("defaults")] public Dictionary<string, bool> Defaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("devnode_id")] public string? DevnodeId { get; set; }
    [JsonPropertyName("resolution_method")] public string ResolutionMethod { get; set; } = "unresolved";
    [JsonPropertyName("properties")] public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DeviceNode
{
    [JsonPropertyName("instance_id")] public string InstanceId { get; set; } = string.Empty;
    [JsonPropertyName("parent_id")] public string? ParentId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("properties")] public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("location_paths")] public List<string> LocationPaths { get; set; } = [];
    [JsonPropertyName("container_id")] public string? ContainerId { get; set; }
}

public sealed class JackRecord
{
    [JsonPropertyName("endpoint_id")] public string EndpointId { get; set; } = string.Empty;
    [JsonPropertyName("jack")] public Dictionary<string, JsonElement> Jack { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("sink")] public Dictionary<string, JsonElement>? Sink { get; set; }
}

public sealed class DisplayPath
{
    [JsonPropertyName("path_id")] public string PathId { get; set; } = string.Empty;
    [JsonPropertyName("output_technology")] public long? OutputTechnology { get; set; }
    [JsonPropertyName("connector_instance")] public int? ConnectorInstance { get; set; }
    [JsonPropertyName("target_name")] public string? TargetName { get; set; }
    [JsonPropertyName("adapter_name")] public string? AdapterName { get; set; }
    [JsonPropertyName("adapter_instance_id")] public string? AdapterInstanceId { get; set; }
    [JsonPropertyName("edid_manufacturer_id")] public int? EdidManufacturerId { get; set; }
    [JsonPropertyName("edid_product_code_id")] public int? EdidProductCodeId { get; set; }
    [JsonPropertyName("adapter_luid")] public Dictionary<string, JsonElement>? AdapterLuid { get; set; }
    [JsonPropertyName("properties")] public Dictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class SnapshotJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static Snapshot Read(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<Snapshot>(json, Options)
            ?? throw new JsonException("快照內容為空");
    }
}

internal static class JsonValue
{
    public static string? String(Dictionary<string, JsonElement> values, string key)
    {
        return values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static int? Integer(Dictionary<string, JsonElement> values, string key)
    {
        return values.TryGetValue(key, out var value) && value.TryGetInt32(out var result) ? result : null;
    }

    public static bool StructuredEquals(JsonElement left, IReadOnlyDictionary<string, JsonElement> right)
    {
        if (left.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        foreach (var property in left.EnumerateObject())
        {
            if (!right.TryGetValue(property.Name, out var rightValue) || rightValue.GetRawText() != property.Value.GetRawText())
            {
                return false;
            }
        }
        return left.EnumerateObject().Count() == right.Count;
    }
}
