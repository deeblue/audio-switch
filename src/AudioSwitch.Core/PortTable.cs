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

    public static PortTable Empty(MachineIdentity? machine = null) => new(new PortTableDocument { Machine = machine });

    public void AddInterface(PortDefinition port)
    {
        EnsureEnabled();
        ArgumentNullException.ThrowIfNull(port);
        if (string.IsNullOrWhiteSpace(port.Id) || string.IsNullOrWhiteSpace(port.Name) || string.IsNullOrWhiteSpace(port.Type))
            throw new ArgumentException(Loc.T("接口 ID、名稱與類型不可為空。"), nameof(port));
        if (FindInterface(port.Id) is not null)
            throw new ArgumentException(Loc.F("接口 ID 已存在：{0}", port.Id), nameof(port));
        _document.Interfaces.Add(port);
        MoveInterface(port.Id, port.Edge ?? "left", port.Order ?? int.MaxValue);
    }

    public void RenameInterface(string interfaceId, string name)
    {
        EnsureEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var port = FindInterface(interfaceId) ?? throw new ArgumentException(Loc.F("找不到接口 ID：{0}", interfaceId), nameof(interfaceId));
        port.Name = name.Trim();
    }

    public void MoveInterface(string interfaceId, string edge, int order)
    {
        EnsureEnabled();
        ArgumentException.ThrowIfNullOrWhiteSpace(edge);
        if (edge is not ("left" or "right" or "rear" or "front"))
            throw new ArgumentOutOfRangeException(nameof(edge), Loc.T("接口邊必須是 left、right、rear 或 front。"));
        var port = FindInterface(interfaceId) ?? throw new ArgumentException(Loc.F("找不到接口 ID：{0}", interfaceId), nameof(interfaceId));
        var oldEdge = port.Edge;
        var oldEdgePorts = _document.Interfaces.Where(item => item.Edge == oldEdge && item.Id != interfaceId).OrderBy(item => item.Order).ToList();
        for (var index = 0; index < oldEdgePorts.Count; index++) oldEdgePorts[index].Order = index;
        var targetEdgePorts = _document.Interfaces.Where(item => item.Edge == edge && item.Id != interfaceId).OrderBy(item => item.Order).ToList();
        var targetIndex = Math.Clamp(order, 0, targetEdgePorts.Count);
        port.Edge = edge;
        targetEdgePorts.Insert(targetIndex, port);
        for (var index = 0; index < targetEdgePorts.Count; index++) targetEdgePorts[index].Order = index;
        _document.Interfaces = _document.Interfaces.OrderBy(item => item.Edge, StringComparer.Ordinal)
            .ThenBy(item => item.Order).ToList();
    }

    public bool RemoveInterface(string interfaceId)
    {
        EnsureEnabled();
        var port = FindInterface(interfaceId);
        if (port is null) return false;
        _document.Interfaces.Remove(port);
        var siblings = _document.Interfaces.Where(item => item.Edge == port.Edge).OrderBy(item => item.Order).ToList();
        for (var index = 0; index < siblings.Count; index++) siblings[index].Order = index;
        return true;
    }

    public void BindMachine(MachineIdentity machine)
    {
        EnsureEnabled();
        ArgumentNullException.ThrowIfNull(machine);
        _document.Machine ??= machine;
    }

    public static PortTable Read(string json, MachineIdentity? currentMachine = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var payload = JsonDocument.Parse(json);
        if (payload.RootElement.ValueKind != JsonValueKind.Object ||
            !payload.RootElement.TryGetProperty("schema_version", out var schemaVersion) || !schemaVersion.TryGetInt32(out var version) || version != 1 ||
            !payload.RootElement.TryGetProperty("interfaces", out var interfaces) || interfaces.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(Loc.T("接口表格式錯誤或 schema_version 不支援"));
        }
        var document = JsonSerializer.Deserialize<PortTableDocument>(json, JsonOptions)
            ?? throw new JsonException(Loc.T("接口表內容為空"));
        if (document.SchemaVersion != 1 || document.Interfaces is null)
            throw new JsonException(Loc.T("接口表格式錯誤或 schema_version 不支援"));
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

        var port = FindInterface(interfaceId) ?? throw new ArgumentException(Loc.F("找不到接口 ID：{0}", interfaceId), nameof(interfaceId));
        var diff = AttachmentDetector.Detect(before, after);
        var transports = diff.Select(AttachmentDetector.Transport).ToArray();
        var beforePathIds = before.DisplayPaths.Select(path => path.PathId).ToHashSet(StringComparer.Ordinal);
        var addedPaths = after.DisplayPaths.Where(path => !beforePathIds.Contains(path.PathId)).ToArray();
        if (diff.Count > 0 && transports.Distinct(StringComparer.Ordinal).Count() == transports.Length)
        {
            // Screens on a dock arrive with its USB devices. Screens on a USB display adapter are placed through
            // the USB tree; the others run on the host GPU and are recorded as display connectors of this port.
            var dockConnectors = CarriesScreens(port.Type)
                ? addedPaths
                    .Where(path => path.AdapterInstanceId?.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) != true)
                    .Select(StableDisplayConnector)
                    .OfType<DisplayConnector>()
                    .ToArray()
                : [];
            foreach (var connector in dockConnectors)
                AddDisplayConnector(port, connector);
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

            return new CalibrationResult("calibrated", interfaceId, transports, rows.SelectMany(row => row.LocationPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), rows, [], dockConnectors);
        }

        // An HDMI connector carries one screen; USB-C and DisplayPort can carry several through a dock or MST hub; USB-A none.
        if (diff.Count == 0 && CarriesScreens(port.Type) && (addedPaths.Length == 1 || (addedPaths.Length > 1 && port.Type != "hdmi")))
        {
            var connectors = addedPaths.Select(StableDisplayConnector).ToArray();
            if (connectors.Any(connector => connector is null))
            {
                return new CalibrationResult("no_candidate", null, [], [], [], addedPaths.Select(path => path.PathId).ToArray());
            }

            foreach (var connector in connectors)
                AddDisplayConnector(port, connector!);
            return new CalibrationResult("calibrated", interfaceId, [], [], [], addedPaths.Select(path => path.PathId).ToArray(), connectors!);
        }

        if (diff.Count == 0 && addedPaths.Length > 0 && CarriesScreens(port.Type))
        {
            return new CalibrationResult("ambiguous", null, [], [], [], addedPaths.Select(path => path.PathId).ToArray());
        }

        return new CalibrationResult(diff.Count > 0 ? "ambiguous" : "no_candidate", null, [], [], [], diff.Select(node => node.InstanceId).ToArray());
    }

    // Types that can carry a screen; an unknown type keeps working as before.
    private static bool CarriesScreens(string type) => type is not ("usb-a" or "audio-jack");

    private static DisplayConnector? StableDisplayConnector(DisplayPath path)
    {
        var connector = new DisplayConnector
        {
            AdapterDevicePath = path.AdapterName,
            TargetId = JsonValue.Integer(path.Properties, "target_id"),
            ConnectorInstance = path.ConnectorInstance,
            OutputTechnology = path.OutputTechnology
        };
        return string.IsNullOrWhiteSpace(connector.AdapterDevicePath) || !connector.TargetId.HasValue ||
            !connector.ConnectorInstance.HasValue || !connector.OutputTechnology.HasValue
            ? null
            : connector;
    }

    private static void AddDisplayConnector(PortDefinition port, DisplayConnector connector)
    {
        if (port.DisplayConnectors.Any(existing => existing.AdapterDevicePath == connector.AdapterDevicePath &&
            existing.TargetId == connector.TargetId && existing.ConnectorInstance == connector.ConnectorInstance &&
            existing.OutputTechnology == connector.OutputTechnology))
        {
            return;
        }

        port.DisplayConnectors.Add(connector);
        port.CalibrationSources.Add(new PortCalibrationSource { Source = "校準", DisplayConnector = connector });
    }

    public CalibrationResult CalibrateExistingEndpoint(string interfaceId, Snapshot snapshot, string endpointId)
    {
        EnsureEnabled();
        ArgumentNullException.ThrowIfNull(snapshot);
        var port = FindInterface(interfaceId) ?? throw new ArgumentException(Loc.F("找不到接口 ID：{0}", interfaceId), nameof(interfaceId));
        var attachment = AttachmentDetector.FindCurrentPathAttachment(snapshot, endpointId);
        if (attachment is null || attachment.LocationPaths.Count == 0)
            return new CalibrationResult("no_candidate", null, [], [], [], [endpointId]);
        var transport = AttachmentDetector.Transport(attachment);
        var existing = port.CalibrationSources.Where(source => string.Equals(source.Transport, transport, StringComparison.OrdinalIgnoreCase)).ToArray();
        var paths = attachment.LocationPaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (existing.Length > 0)
            return existing.Any(source => source.LocationPaths.Order(StringComparer.Ordinal).SequenceEqual(paths, StringComparer.Ordinal))
                ? new CalibrationResult("calibrated", interfaceId, [transport], paths, [new CalibratedAttachment("現有裝置", transport, attachment.InstanceId, paths)], [])
                : new CalibrationResult("ambiguous", null, [], [], [], [attachment.InstanceId]);
        foreach (var path in paths)
            if (!port.UsbLocationPathPrefixes.Contains(path, StringComparer.Ordinal)) port.UsbLocationPathPrefixes.Add(path);
        port.CalibrationSources.Add(new PortCalibrationSource { Source = "現有裝置", Transport = transport, InstanceId = attachment.InstanceId, LocationPaths = paths.ToList() });
        return new CalibrationResult("calibrated", interfaceId, [transport], paths, [new CalibratedAttachment("現有裝置", transport, attachment.InstanceId, paths)], []);
    }

    private static string? GetMachineMismatch(MachineIdentity? expected, MachineIdentity? actual)
    {
        if (expected is null || actual is null)
        {
            return null;
        }

        if (HasDifferentValue(expected.Manufacturer, actual.Manufacturer) || HasDifferentValue(expected.Model, actual.Model))
        {
            return Loc.T("主機識別不符，已停用接口校準");
        }

        if (!string.IsNullOrWhiteSpace(expected.MtM) && !string.IsNullOrWhiteSpace(actual.MtM) &&
            !actual.MtM.StartsWith(expected.MtM, StringComparison.OrdinalIgnoreCase))
        {
            return Loc.T("主機識別不符，已停用接口校準");
        }

        return null;
    }

    private static bool HasDifferentValue(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(actual) &&
        !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private void EnsureEnabled()
    {
        if (!IsEnabled) throw new InvalidOperationException(Warning);
    }
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
    IReadOnlyList<DisplayConnector>? DisplayConnectors = null);
