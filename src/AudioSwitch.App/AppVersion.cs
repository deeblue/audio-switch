using System.Reflection;

namespace AudioSwitch.App;

/// <summary>Version shown to the user, so different builds of the same version can be told apart.</summary>
public static class AppVersion
{
    private static readonly Assembly Assembly = typeof(AppVersion).Assembly;

    public static string Version { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "未知";

    public static string BuildText { get; } = FormatBuild(Metadata("BuildCommit"), Metadata("BuildHasLocalChanges") == "true", Metadata("BuildTime"));

    public static string DisplayText { get; } = string.IsNullOrEmpty(BuildText) ? $"版本 {Version}" : $"版本 {Version}（{BuildText}）";

    internal static string FormatBuild(string? commit, bool hasLocalChanges, string? buildTime)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(commit)) parts.Add(hasLocalChanges ? $"{commit} 含未提交修改" : commit);
        if (!string.IsNullOrWhiteSpace(buildTime)) parts.Add($"建置於 {buildTime}");
        return string.Join("，", parts);
    }

    private static string? Metadata(string key) =>
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value;
}
