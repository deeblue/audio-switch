using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSwitch.Core;

public sealed class AudioSwitchConfiguration
{
    public required PortTable Ports { get; init; }
    public required AliasStore Aliases { get; init; }
    public required AudioSwitchSettings Settings { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    internal HashSet<string> ProtectedFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AudioSwitchSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? PlaybackHotkey { get; set; }
    public string? RecordingHotkey { get; set; }
    public bool AutostartEnabled { get; set; }
    public bool FirstRunNotificationShown { get; set; }
}

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

public static class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AudioSwitchConfiguration Load(string directory, MachineIdentity? machine = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var errors = new List<string>();
        var portsPath = Path.Combine(directory, "ports.json");
        var aliasesPath = Path.Combine(directory, "aliases.json");
        var settingsPath = Path.Combine(directory, "settings.json");
        var protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var ports = LoadPart(portsPath, () => PortTable.Empty(), json => PortTable.Read(json, machine), errors, protectedFiles);
        var aliases = LoadPart(aliasesPath, () => new AliasStore(), json => ReadAliases(json), errors, protectedFiles);
        var settings = LoadPart(settingsPath, () => new AudioSwitchSettings(), json => ReadSettings(json), errors, protectedFiles);
        return new AudioSwitchConfiguration
        {
            Ports = ports,
            Aliases = aliases,
            Settings = settings,
            Errors = errors,
            ProtectedFiles = protectedFiles
        };
    }

    public static void Save(string directory, AudioSwitchConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(directory);
        SavePart(directory, "ports.json", configuration.Ports.ToJson(), configuration);
        SavePart(directory, "aliases.json", WriteAliases(configuration.Aliases), configuration);
        SavePart(directory, "settings.json", WriteSettings(configuration.Settings), configuration);
    }

    public static void WriteAtomic(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("設定檔路徑必須包含資料夾", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void SavePart(string directory, string fileName, string content, AudioSwitchConfiguration configuration)
    {
        if (!configuration.ProtectedFiles.Contains(fileName))
        {
            WriteAtomic(Path.Combine(directory, fileName), content);
        }
    }

    private static T LoadPart<T>(string path, Func<T> empty, Func<string, T> read, ICollection<string> errors,
        ISet<string> protectedFiles)
    {
        if (!File.Exists(path))
        {
            return empty();
        }

        try
        {
            return read(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            var fileName = Path.GetFileName(path);
            var archivePath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                File.Move(path, archivePath);
                errors.Add($"{fileName}：{exception.Message}；原始檔已保留為 {Path.GetFileName(archivePath)}。");
            }
            catch (Exception archiveException) when (archiveException is IOException or UnauthorizedAccessException)
            {
                protectedFiles.Add(fileName);
                errors.Add($"{fileName}：{exception.Message}；無法封存原始檔（{archiveException.Message}），本次執行期間不會覆寫此檔。");
            }
            return empty();
        }
    }

    private static AliasStore ReadAliases(string json)
    {
        EnsureSchemaVersion(json);
        using var payload = JsonDocument.Parse(json);
        if (!payload.RootElement.TryGetProperty("original_device_descriptions", out var originals) || originals.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("別名設定缺少原始端點名稱對照");
        }
        var document = JsonSerializer.Deserialize<AliasDocument>(json, JsonOptions)
            ?? throw new JsonException("別名設定內容為空");
        if (document.SchemaVersion != 1 || document.OriginalDeviceDescriptions is null)
        {
            throw new JsonException("別名設定格式錯誤或 schema_version 不支援");
        }
        return new AliasStore(new Dictionary<string, string>(document.OriginalDeviceDescriptions, StringComparer.OrdinalIgnoreCase));
    }

    private static AudioSwitchSettings ReadSettings(string json)
    {
        EnsureSchemaVersion(json);
        var settings = JsonSerializer.Deserialize<AudioSwitchSettings>(json, JsonOptions)
            ?? throw new JsonException("一般設定內容為空");
        if (settings.SchemaVersion != 1)
        {
            throw new JsonException("一般設定格式錯誤或 schema_version 不支援");
        }
        return settings;
    }

    private static void EnsureSchemaVersion(string json)
    {
        using var payload = JsonDocument.Parse(json);
        if (payload.RootElement.ValueKind != JsonValueKind.Object ||
            !payload.RootElement.TryGetProperty("schema_version", out var schemaVersion) ||
            !schemaVersion.TryGetInt32(out var version) || version != 1)
        {
            throw new JsonException("設定檔格式錯誤或 schema_version 不支援");
        }
    }

    private static string WriteAliases(AliasStore aliases) => JsonSerializer.Serialize(
        new AliasDocument { OriginalDeviceDescriptions = new Dictionary<string, string>(aliases.OriginalDescriptions, StringComparer.OrdinalIgnoreCase) }, JsonOptions) + "\n";

    private static string WriteSettings(AudioSwitchSettings settings) => JsonSerializer.Serialize(settings, JsonOptions) + "\n";

    private sealed class AliasDocument
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("original_device_descriptions")] public Dictionary<string, string> OriginalDeviceDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
