using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSwitch.Core;

public sealed class PortTable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private PortTableDocument _document;

    private PortTable(PortTableDocument document, string? warning = null)
    {
        _document = document;
        Warning = warning;
    }

    public IReadOnlyList<PortDefinition> Interfaces => _document.Interfaces;
    public MachineIdentity? Machine => _document.Machine;
    public bool IsEnabled => Warning is null;
    public string? Warning { get; }

    public static PortTable Empty() => new(new PortTableDocument());

    public static PortTable Read(string json, MachineIdentity? currentMachine = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var payload = JsonDocument.Parse(json);
        if (payload.RootElement.ValueKind != JsonValueKind.Object ||
            !payload.RootElement.TryGetProperty("schema_version", out var schemaVersion) || !schemaVersion.TryGetInt32(out var version) || version != 1 ||
            !payload.RootElement.TryGetProperty("interfaces", out var interfaces) || interfaces.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("接口表格式錯誤或 schema_version 不支援");
        }
        var document = JsonSerializer.Deserialize<PortTableDocument>(json, JsonOptions)
            ?? throw new JsonException("接口表內容為空");
        if (document.SchemaVersion != 1 || document.Interfaces is null)
            throw new JsonException("接口表格式錯誤或 schema_version 不支援");
        return new PortTable(document, GetMachineMismatch(document.Machine, currentMachine));
    }

    public string ToJson() => JsonSerializer.Serialize(_document, JsonOptions) + "\n";

    public PortDefinition? FindInterface(string interfaceId) =>
        _document.Interfaces.FirstOrDefault(port => string.Equals(port.Id, interfaceId, StringComparison.Ordinal));

    internal PortDefinition? FindForLocationPath(string locationPath)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var matches = _document.Interfaces
            .SelectMany(port => port.UsbLocationPathPrefixes
                .Where(prefix => locationPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(prefix => (Port: port, Prefix: prefix)))
            .OrderByDescending(match => match.Prefix.Length)
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var longestLength = matches[0].Prefix.Length;
        var longest = matches.Where(match => match.Prefix.Length == longestLength).Select(match => match.Port.Id).Distinct(StringComparer.Ordinal).ToArray();
        return longest.Length == 1 ? matches[0].Port : null;
    }

    public CalibrationResult Calibrate(string interfaceId, Snapshot before, Snapshot after)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException(Warning);
        }

        var port = FindInterface(interfaceId) ?? throw new ArgumentException($"找不到接口 ID：{interfaceId}", nameof(interfaceId));
        var diff = AttachmentDetector.Detect(before, after);
        var transports = diff.Select(AttachmentDetector.Transport).ToArray();
        if (diff.Count > 0 && transports.Distinct(StringComparer.Ordinal).Count() == transports.Length)
        {
            var rows = new List<CalibratedAttachment>();
            for (var index = 0; index < diff.Count; index++)
            {
                var node = diff[index];
                var paths = node.LocationPaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                foreach (var path in paths)
                {
                    if (!port.UsbLocationPathPrefixes.Contains(path, StringComparer.Ordinal))
                    {
                        port.UsbLocationPathPrefixes.Add(path);
                    }
                }
                rows.Add(new CalibratedAttachment("校準", transports[index], node.InstanceId, paths));
                port.CalibrationSources.Add(new PortCalibrationSource { Source = "校準", Transport = transports[index], InstanceId = node.InstanceId, LocationPaths = paths.ToList() });
            }

            return new CalibrationResult("calibrated", interfaceId, transports, rows.SelectMany(row => row.LocationPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), rows, []);
        }

        if (diff.Count == 0)
        {
            var beforePathIds = before.DisplayPaths.Select(path => path.PathId).ToHashSet(StringComparer.Ordinal);
            var addedPaths = after.DisplayPaths.Where(path => !beforePathIds.Contains(path.PathId)).ToArray();
            if (addedPaths.Length == 1)
            {
                var path = addedPaths[0];
                var connector = new DisplayConnector
                {
                    AdapterDevicePath = path.AdapterName,
                    TargetId = JsonValue.Integer(path.Properties, "target_id"),
                    ConnectorInstance = path.ConnectorInstance,
                    OutputTechnology = path.OutputTechnology
                };
                if (string.IsNullOrWhiteSpace(connector.AdapterDevicePath) || !connector.TargetId.HasValue ||
                    !connector.ConnectorInstance.HasValue || !connector.OutputTechnology.HasValue)
                {
                    return new CalibrationResult("no_candidate", null, [], [], [], [path.PathId]);
                }

                if (!port.DisplayConnectors.Any(existing => existing.AdapterDevicePath == connector.AdapterDevicePath &&
                    existing.TargetId == connector.TargetId && existing.ConnectorInstance == connector.ConnectorInstance &&
                    existing.OutputTechnology == connector.OutputTechnology))
                {
                    port.DisplayConnectors.Add(connector);
                    port.CalibrationSources.Add(new PortCalibrationSource { Source = "校準", DisplayConnector = connector });
                }
                return new CalibrationResult("calibrated", interfaceId, [], [], [], [path.PathId], connector);
            }
            if (addedPaths.Length > 0)
            {
                return new CalibrationResult("ambiguous", null, [], [], [], addedPaths.Select(path => path.PathId).ToArray());
            }
        }

        return new CalibrationResult(diff.Count > 0 ? "ambiguous" : "no_candidate", null, [], [], [], diff.Select(node => node.InstanceId).ToArray());
    }

    private static string? GetMachineMismatch(MachineIdentity? expected, MachineIdentity? actual)
    {
        if (expected is null || actual is null)
        {
            return null;
        }

        if (HasDifferentValue(expected.Manufacturer, actual.Manufacturer) || HasDifferentValue(expected.Model, actual.Model))
        {
            return "主機識別不符，已停用接口校準";
        }

        if (!string.IsNullOrWhiteSpace(expected.MtM) && !string.IsNullOrWhiteSpace(actual.MtM) &&
            !actual.MtM.StartsWith(expected.MtM, StringComparison.OrdinalIgnoreCase))
        {
            return "主機識別不符，已停用接口校準";
        }

        return null;
    }

    private static bool HasDifferentValue(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(actual) &&
        !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}

internal sealed class PortTableDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("machine")] public MachineIdentity? Machine { get; set; }
    [JsonPropertyName("interfaces")] public List<PortDefinition> Interfaces { get; set; } = [];
}

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

public sealed class DisplayConnector
{
    [JsonPropertyName("adapter_device_path")] public string? AdapterDevicePath { get; set; }
    [JsonPropertyName("target_id")] public int? TargetId { get; set; }
    [JsonPropertyName("connector_instance")] public int? ConnectorInstance { get; set; }
    [JsonPropertyName("output_technology")] public long? OutputTechnology { get; set; }
}

public sealed class PortCalibrationSource
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("transport")] public string? Transport { get; set; }
    [JsonPropertyName("instance_id")] public string? InstanceId { get; set; }
    [JsonPropertyName("location_paths")] public List<string> LocationPaths { get; set; } = [];
    [JsonPropertyName("display_connector")] public DisplayConnector? DisplayConnector { get; set; }
}

public sealed record CalibratedAttachment(string Source, string Transport, string InstanceId, IReadOnlyList<string> LocationPaths);

public sealed record CalibrationResult(
    string Status,
    string? InterfaceId,
    IReadOnlyList<string> Transports,
    IReadOnlyList<string> LocationPaths,
    IReadOnlyList<CalibratedAttachment> Attachments,
    IReadOnlyList<string> CandidateIds,
    DisplayConnector? DisplayConnector = null);
