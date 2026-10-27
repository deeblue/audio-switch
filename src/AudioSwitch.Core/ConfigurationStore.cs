using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioSwitch.Core;

/// <summary>Everything loaded from the settings folder, with the errors met while loading.</summary>
public sealed class AudioSwitchConfiguration
{
    public required PortTable Ports { get; init; }
    public required AliasStore Aliases { get; init; }
    public required AudioSwitchSettings Settings { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    internal HashSet<string> ProtectedFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Loads and saves the settings files in %APPDATA%\AudioSwitch. A file that cannot be read is archived and replaced by defaults; if it cannot be archived, it is not overwritten while the app runs.</summary>
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

        var ports = LoadPart(portsPath, () => PortTable.Empty(machine), json => PortTable.Read(json, machine), errors, protectedFiles);
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
        var directory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException(Loc.T("設定檔路徑必須包含資料夾"), nameof(path));
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
                errors.Add(Loc.F("{0}：{1}；原始檔已保留為 {2}。", fileName, exception.Message, Path.GetFileName(archivePath)));
            }
            catch (Exception archiveException) when (archiveException is IOException or UnauthorizedAccessException)
            {
                protectedFiles.Add(fileName);
                errors.Add(Loc.F("{0}：{1}；無法封存原始檔（{2}），本次執行期間不會覆寫此檔。", fileName, exception.Message, archiveException.Message));
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
            throw new JsonException(Loc.T("別名設定缺少原始端點名稱對照"));
        }
        var document = JsonSerializer.Deserialize<AliasDocument>(json, JsonOptions)
            ?? throw new JsonException(Loc.T("別名設定內容為空"));
        if (document.SchemaVersion != 1 || document.OriginalDeviceDescriptions is null)
        {
            throw new JsonException(Loc.T("別名設定格式錯誤或 schema_version 不支援"));
        }
        return new AliasStore(new Dictionary<string, string>(document.OriginalDeviceDescriptions, StringComparer.OrdinalIgnoreCase));
    }

    private static AudioSwitchSettings ReadSettings(string json)
    {
        EnsureSchemaVersion(json);
        var settings = JsonSerializer.Deserialize<AudioSwitchSettings>(json, JsonOptions)
            ?? throw new JsonException(Loc.T("一般設定內容為空"));
        if (settings.SchemaVersion != 1)
        {
            throw new JsonException(Loc.T("一般設定格式錯誤或 schema_version 不支援"));
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
            throw new JsonException(Loc.T("設定檔格式錯誤或 schema_version 不支援"));
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
