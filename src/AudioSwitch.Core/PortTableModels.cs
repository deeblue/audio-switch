using System.Text.Json.Serialization;

namespace AudioSwitch.Core;

/// <summary>The JSON document of ports.json.</summary>
internal sealed class PortTableDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("machine")] public MachineIdentity? Machine { get; set; }
    [JsonPropertyName("interfaces")] public List<PortDefinition> Interfaces { get; set; } = [];
}

/// <summary>One port on the drawing: type, edge and order, name, and what calibration recorded for it.</summary>
public sealed class PortDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("position")] public string? Position { get; set; }
    [JsonPropertyName("edge")] public string? Edge { get; set; }
    [JsonPropertyName("order")] public int? Order { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("position_source")] public string? PositionSource { get; set; }
    [JsonPropertyName("usb_location_path_prefixes")] public List<string> UsbLocationPathPrefixes { get; set; } = [];
    [JsonPropertyName("display_connectors")] public List<DisplayConnector> DisplayConnectors { get; set; } = [];
    [JsonPropertyName("calibration_sources")] public List<PortCalibrationSource> CalibrationSources { get; set; } = [];
}

/// <summary>The stable identity of a screen output. Path IDs, adapter LUIDs and DISPLAYn names change on reconnect and are not used.</summary>
public sealed class DisplayConnector
{
    [JsonPropertyName("adapter_device_path")] public string? AdapterDevicePath { get; set; }
    [JsonPropertyName("target_id")] public int? TargetId { get; set; }
    [JsonPropertyName("connector_instance")] public int? ConnectorInstance { get; set; }
    [JsonPropertyName("output_technology")] public long? OutputTechnology { get; set; }
}

/// <summary>One recorded piece of evidence for a port: a USB attachment with its transport and paths, or a screen connector. Source tells how it was found and is stored as written.</summary>
public sealed class PortCalibrationSource
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("transport")] public string? Transport { get; set; }
    [JsonPropertyName("instance_id")] public string? InstanceId { get; set; }
    [JsonPropertyName("location_paths")] public List<string> LocationPaths { get; set; } = [];
    [JsonPropertyName("display_connector")] public DisplayConnector? DisplayConnector { get; set; }
}

/// <summary>A USB attachment recorded by calibration.</summary>
public sealed record CalibratedAttachment(string Source, string Transport, string InstanceId, IReadOnlyList<string> LocationPaths);

/// <summary>The outcome of a calibration: calibrated, ambiguous (several changes) or no_candidate (nothing new), with what was recorded.</summary>
public sealed record CalibrationResult(
    string Status,
    string? InterfaceId,
    IReadOnlyList<string> Transports,
    IReadOnlyList<string> LocationPaths,
    IReadOnlyList<CalibratedAttachment> Attachments,
    IReadOnlyList<string> CandidateIds,
    IReadOnlyList<DisplayConnector>? DisplayConnectors = null);
